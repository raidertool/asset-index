#!/usr/bin/env python3
"""Disk exhaustion, cleanup, and log-boundary checks without game access."""
from contextlib import redirect_stdout
import importlib.util
import io
import os
from pathlib import Path
import signal
import subprocess
import sys
import tempfile
import time
from types import SimpleNamespace
import unittest
from unittest.mock import Mock, patch

ROOT = Path(__file__).resolve().parents[2]
SCRIPT = ROOT / "scripts/ci/with-disk-reserve.py"
SPEC = importlib.util.spec_from_file_location("disk_reserve", SCRIPT)
monitor = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(monitor)


class DiskReserveTests(unittest.TestCase):
    def run_mock(self, free, process):
        self.output = io.StringIO()
        with patch.object(monitor.shutil, "disk_usage", side_effect=[SimpleNamespace(free=value) for value in free]), \
             patch.object(monitor.subprocess, "Popen", return_value=process) as launch, \
             patch.object(monitor, "stop_group") as stop, \
             patch.object(monitor.time, "sleep"), redirect_stdout(self.output):
            self.launch, self.stop = launch, stop
            return monitor.run(["dummy-command"], Path("/runner-temp"), None)

    def test_streaming_can_start_with_less_than_whole_game_size(self):
        process = Mock(pid=123, poll=Mock(side_effect=[None, 0, 0]))
        self.assertEqual(self.run_mock([3 * monitor.GIB, 3 * monitor.GIB, 2 * monitor.GIB], process), 0)
        self.assertIn("minimum free 2.00 GiB; peak added usage 1.00 GiB", self.output.getvalue())
        self.stop.assert_called_once_with(process)
        self.assertEqual(self.launch.call_args.kwargs["env"]["TMPDIR"], "/runner-temp")
        self.assertTrue(self.launch.call_args.kwargs["start_new_session"])

    def test_initial_reserve_failure_never_starts_command(self):
        with self.assertRaisesRegex(RuntimeError, "not started"):
            self.run_mock([monitor.RESERVE - 1], Mock())
        self.launch.assert_not_called()

    def test_breach_during_command_stops_only_its_group(self):
        process = Mock(pid=123, poll=Mock(return_value=None))
        with self.assertRaisesRegex(RuntimeError, "without publishing"):
            self.run_mock([3 * monitor.GIB, 3 * monitor.GIB, monitor.RESERVE - 1], process)
        self.stop.assert_called_once_with(process)

    def test_final_low_disk_sample_rejects_even_successful_command(self):
        process = Mock(pid=123, poll=Mock(return_value=0))
        with self.assertRaisesRegex(RuntimeError, "without publishing"):
            self.run_mock([3 * monitor.GIB, monitor.RESERVE - 1], process)
        self.stop.assert_called_once_with(process)

    def test_child_failure_and_signal_status_remain_failures(self):
        for child_status, expected in [(23, 23), (-signal.SIGTERM, 143)]:
            with self.subTest(child_status=child_status):
                self.assertEqual(self.run_mock([3 * monitor.GIB] * 2,
                                              Mock(poll=Mock(return_value=child_status))), expected)

    def test_disk_stat_failure_still_cleans_up(self):
        process = Mock(pid=123, poll=Mock(return_value=None))
        with patch.object(monitor.shutil, "disk_usage", side_effect=[SimpleNamespace(free=3 * monitor.GIB), OSError()]), \
             patch.object(monitor.subprocess, "Popen", return_value=process), \
             patch.object(monitor, "stop_group") as stop, redirect_stdout(io.StringIO()):
            with self.assertRaises(OSError):
                monitor.run(["dummy-command"], Path("/runner-temp"), None)
        stop.assert_called_once_with(process)

    def test_cleanup_has_grace_then_kills_remaining_own_group(self):
        process = Mock(pid=123, wait=Mock(side_effect=[subprocess.TimeoutExpired("dummy", 30), 0]))
        with patch.object(monitor.os, "killpg") as kill, patch.object(monitor.time, "monotonic", side_effect=[0, 31]):
            monitor.stop_group(process)
        self.assertEqual(kill.call_args_list, [((123, signal.SIGTERM),), ((123, signal.SIGKILL),)])
        self.assertEqual(process.wait.call_args_list, [((), {"timeout": 30}), ((), {"timeout": 5})])

    def test_already_gone_group_is_safe(self):
        process = Mock(pid=123)
        with patch.object(monitor.os, "killpg", side_effect=ProcessLookupError()):
            monitor.stop_group(process)
        process.wait.assert_not_called()

    def test_finished_parent_still_allows_descendant_cleanup(self):
        process = Mock(pid=123, wait=Mock(return_value=0))
        with patch.object(monitor.os, "killpg", side_effect=[None, None, ProcessLookupError()]) as kill, \
             patch.object(monitor.time, "monotonic", side_effect=[0, 1, 2]), \
             patch.object(monitor.time, "sleep") as sleep:
            monitor.stop_group(process)
        self.assertEqual(kill.call_args_list, [((123, signal.SIGTERM),), ((123, 0),), ((123, 0),)])
        sleep.assert_called_once_with(0.1)

    def test_cli_termination_reaches_child_cleanup(self):
        with tempfile.TemporaryDirectory() as directory:
            temp = Path(directory)
            if monitor.shutil.disk_usage(temp).free < monitor.RESERVE:
                self.skipTest("Local disk lacks the reserve for this real subprocess check.")
            command = '''trap 'touch "$TMPDIR/cleaned"; exit 0' TERM
echo $$ > "$TMPDIR/childpid"
touch "$TMPDIR/ready"
while :; do sleep 1; done'''
            process = subprocess.Popen([sys.executable, str(SCRIPT), "--log", str(temp / "private.log"),
                                        "--", "bash", "-c", command], env={**os.environ, "RUNNER_TEMP": directory},
                                       stdout=subprocess.PIPE, stderr=subprocess.PIPE, text=True)
            try:
                deadline = time.monotonic() + 5
                while not (temp / "ready").exists() and process.poll() is None and time.monotonic() < deadline:
                    time.sleep(0.01)
                self.assertTrue((temp / "ready").exists())
                process.terminate()
                stdout, stderr = process.communicate(timeout=10)
                self.assertEqual(process.returncode, 143)
                self.assertTrue((temp / "cleaned").exists())
                self.assertNotIn(directory, stdout + stderr)
                self.assertNotIn("Traceback", stdout + stderr)
            finally:
                if process.poll() is None:
                    process.kill()
                process.wait()
                if (temp / "childpid").exists():
                    try:
                        os.killpg(int((temp / "childpid").read_text()), signal.SIGKILL)
                    except ProcessLookupError:
                        pass

    def test_real_child_runs_cleanup_and_diagnostics_stay_in_local_log(self):
        with tempfile.TemporaryDirectory() as directory:
            temp = Path(directory)
            child = temp / "child.py"
            child.write_text('''import os, signal, sys, time
from pathlib import Path
temp = Path(os.environ["TMPDIR"])
def finish(*_):
    (temp / "cleaned").touch()
    sys.exit(0)
signal.signal(signal.SIGTERM, finish)
print("private-dummy diagnostic", flush=True)
(temp / "ready").touch()
while True:
    time.sleep(0.01)
''')
            def sample(_):
                return SimpleNamespace(free=monitor.GIB if (temp / "ready").exists() else 3 * monitor.GIB)
            output = io.StringIO()
            with patch.object(monitor.shutil, "disk_usage", side_effect=sample), patch.object(monitor, "INTERVAL", 0.01), \
                 redirect_stdout(output), (temp / "private.log").open("w") as log:
                with self.assertRaisesRegex(RuntimeError, "without publishing"):
                    monitor.run([sys.executable, str(child)], temp, log)
            self.assertTrue((temp / "cleaned").exists())
            self.assertIn("private-dummy", (temp / "private.log").read_text())
            self.assertNotIn("private-dummy", output.getvalue())
            self.assertNotIn(directory, output.getvalue())

    def test_cli_error_does_not_echo_command_or_paths(self):
        with tempfile.TemporaryDirectory() as directory:
            result = subprocess.run([sys.executable, str(SCRIPT), "--", "/private-dummy-command"],
                                    env={**os.environ, "RUNNER_TEMP": directory}, capture_output=True, text=True)
        self.assertNotEqual(result.returncode, 0)
        self.assertNotIn("private-dummy", result.stdout + result.stderr)
        self.assertNotIn(directory, result.stdout + result.stderr)
        self.assertNotIn("Traceback", result.stdout + result.stderr)


if __name__ == "__main__":
    unittest.main()
