#!/usr/bin/env python3
"""Exercise the Steam acquisition boundary without a login or FUSE mount."""
import importlib.util
import io
import json
import os
from pathlib import Path
import re
import signal
import subprocess
import sys
import tarfile
import tempfile
import unittest
from unittest.mock import patch

ROOT = Path(__file__).resolve().parents[2]
SCRIPT = ROOT / "scripts/steam/extract.sh"
MANIFEST = "4625131891870974441"
ANNOUNCEMENT = f"mounting app=1808500 depot=1808501 manifest={MANIFEST} at /mock/mount"
MOCK = r'''
import json
import os
from pathlib import Path
import signal
import sys
import time

command = Path(sys.argv[0]).name
args = sys.argv[1:]
temp = Path(os.environ["RUNNER_TEMP"])

def record(event, **details):
    with (temp / "events.jsonl").open("a") as output:
        output.write(json.dumps({"event": event, **details}) + "\n")

def credentials():
    return sorted(key for key in os.environ if key.startswith("STEAM_") and key != "STEAM_DEPOTFS")

def stop_mount(*_):
    record("mount-stopped")
    sys.exit(0)

if command == "timeout":
    os.execvp(args[2], args[2:])
elif command == "sleep":
    time.sleep(0.01)
elif command == "git":
    print(os.environ["MOCK_SOURCE_SHA"])
elif command == "mountpoint":
    sys.exit(0 if (temp / "ready").exists() else 1)
elif command == "fusermount3":
    record("cleanup")
    if os.environ["MOCK_UNMOUNT_FAIL"] == "true":
        sys.exit(1)
    (temp / "ready").unlink(missing_ok=True)
elif command == "dotnet":
    if args[0].endswith("SteamAuthToken.dll"):
        record("auth", credentials=credentials())
        print(os.environ["MOCK_TOKEN"], end="")
        print("private-dummy auth server response", file=sys.stderr)
        sys.exit(int(os.environ["MOCK_AUTH_EXIT"]))
    record("extract", credentials=credentials(), args=args[1:])
    print("private-dummy extraction diagnostics")
    sys.exit(int(os.environ["MOCK_EXTRACT_EXIT"]))
elif command == "SteamDepotFs":
    record("steam", args=args, credentials=credentials())
    signal.signal(signal.SIGTERM, stop_mount)
    print(os.environ["MOCK_ANNOUNCEMENT"], file=sys.stderr, flush=True)
    print("private-dummy depot account log", file=sys.stderr, flush=True)
    if os.environ["MOCK_MOUNT_EXIT"] != "0":
        sys.exit(int(os.environ["MOCK_MOUNT_EXIT"]))
    mount = Path(args[args.index("--mount-point") + 1])
    (mount / "PioneerGame/Content/Paks").mkdir(parents=True)
    if os.environ["MOCK_READY"] == "true":
        (temp / "ready").touch()
    while True:
        time.sleep(1)
else:
    sys.exit(f"Unexpected mock command: {command}")
'''


class PreviewTests(unittest.TestCase):
    def test_nested_time_budgets_leave_validation_and_cleanup_time(self):
        workflow = (ROOT / ".github/workflows/extract.yml").read_text()
        job = workflow.split("\n  extract:\n", 1)[1].split("\n  publish:\n", 1)[0]
        job_budget = int(re.search(r"(?m)^    timeout-minutes: (\d+)$", job)[1])
        steps = re.split(r"(?m)^      - name: ", job)[1:]
        budgets = {}
        for step in steps:
            limit = re.search(r"(?m)^        timeout-minutes: (\d+)$", step)
            self.assertIsNotNone(limit, f"Unbounded extraction step: {step.splitlines()[0]}")
            budgets[step.splitlines()[0]] = int(limit[1])
        script = SCRIPT.read_text()
        mount = int(re.search(r'timeout --kill-after=15s (\d+)m "\$STEAM_DEPOTFS" mount', script)[1])
        extract = int(re.search(r'if timeout --kill-after=15s (\d+)m dotnet', script)[1])
        client = int(re.search(r"--timeout (\d+) --mount-point", script)[1])
        self.assertEqual(client, mount * 60)
        self.assertGreaterEqual(mount, extract + 5)
        self.assertGreaterEqual(budgets["Extract selected Steam release"], mount + 5)
        self.assertGreaterEqual(job_budget, sum(budgets.values()) + 5)

    def run_preview(self, announcement=ANNOUNCEMENT, **overrides):
        with tempfile.TemporaryDirectory() as directory:
            temp = Path(directory)
            binaries = temp / "bin"
            binaries.mkdir()
            for name in ["dotnet", "timeout", "mountpoint", "fusermount3", "sleep", "git", "SteamDepotFs"]:
                mock = binaries / name
                mock.write_text(f"#!{sys.executable}\n" + MOCK)
                mock.chmod(0o755)
            env = {
                **os.environ,
                "PATH": f"{binaries}{os.pathsep}{os.environ['PATH']}",
                "RUNNER_TEMP": directory,
                "SOURCE_DIR": str(ROOT),
                "EXTRACTOR_COMMIT": "1" * 40,
                "MANIFEST_ID": MANIFEST,
                "STEAM_DEPOTFS": str(binaries / "SteamDepotFs"),
                "STEAM_USERNAME": "private-dummy-user",
                "STEAM_PASSWORD": "private-dummy-password",
                "STEAM_ACCESS_TOKEN": "private-dummy-stale-token",
                "STEAM_AUTH_CODE": "private-dummy-code",
                "STEAM_LOGIN_ID": "private-dummy-stale-login",
                "MOCK_SOURCE_SHA": "1" * 40,
                "MOCK_TOKEN": "private-dummy-new-token",
                "MOCK_AUTH_EXIT": "0",
                "MOCK_ANNOUNCEMENT": announcement,
                "MOCK_MOUNT_EXIT": "0",
                "MOCK_EXTRACT_EXIT": "0",
                "MOCK_READY": "true",
                "MOCK_UNMOUNT_FAIL": "false",
                **overrides,
            }
            process = subprocess.Popen(["bash", str(SCRIPT)], env=env, text=True,
                                       stdout=subprocess.PIPE, stderr=subprocess.PIPE, start_new_session=True)
            try:
                stdout, stderr = process.communicate(timeout=30)
            finally:
                # The group contains only this test's subprocesses.
                try:
                    os.killpg(process.pid, signal.SIGKILL)
                except ProcessLookupError:
                    pass
                process.wait()
            events_file = temp / "events.jsonl"
            events = [json.loads(line) for line in events_file.read_text().splitlines()] if events_file.exists() else []
            self.assertNotIn("private-dummy", stdout + stderr)
            if env["MOCK_UNMOUNT_FAIL"] == "true":
                self.assertNotEqual(process.returncode, 0)
                self.assertEqual(len(list(temp.glob("steam-session.*"))), 1)
            else:
                self.assertEqual(list(temp.glob("steam-session.*")), [])
            return process.returncode, events, stdout + stderr

    def test_exact_version_modern_token_and_credential_free_extraction(self):
        for manifest in [MANIFEST, "1", "18446744073709551615"]:
            with self.subTest(manifest=manifest):
                status, events, _ = self.run_preview(ANNOUNCEMENT.replace(MANIFEST, manifest), MANIFEST_ID=manifest)
                self.assertEqual(status, 0)
                self.assertEqual([event["event"] for event in events], ["auth", "steam", "extract", "cleanup", "mount-stopped"])
                self.assertEqual(events[0]["credentials"], ["STEAM_PASSWORD", "STEAM_USERNAME"])
                steam = events[1]
                self.assertEqual(steam["credentials"], ["STEAM_ACCESS_TOKEN", "STEAM_USERNAME"])
                self.assertEqual(steam["args"][steam["args"].index("--manifest") + 1], manifest)
                login_id = steam["args"][steam["args"].index("--login-id") + 1]
                self.assertRegex(login_id, r"^[1-9][0-9]{0,9}$")
                self.assertLessEqual(int(login_id), 2**32 - 1)
                self.assertEqual(events[2]["credentials"], [])

    def test_invalid_inputs_never_authenticate(self):
        invalid = ["", "0", "01", "-1", "18446744073709551616", "100000000000000000000", "abc", "$(echo private-dummy)"]
        for value in invalid:
            with self.subTest(value=value):
                status, events, _ = self.run_preview(MANIFEST_ID=value)
                self.assertNotEqual(status, 0)
                self.assertEqual(events, [])
        for setting in [{"MOCK_SOURCE_SHA": "2" * 40}, {"EXTRACTOR_COMMIT": "main"}, {"STEAM_PASSWORD": ""}]:
            status, events, _ = self.run_preview(**setting)
            self.assertNotEqual(status, 0)
            self.assertEqual(events, [])

    def test_authentication_failure_or_invalid_token_never_mounts(self):
        for setting in [{"MOCK_AUTH_EXIT": "7"}, {"MOCK_TOKEN": ""}, {"MOCK_TOKEN": "bad\ntoken"}, {"MOCK_TOKEN": "bad\rtoken"}]:
            with self.subTest(setting=setting):
                status, events, _ = self.run_preview(**setting)
                self.assertNotEqual(status, 0)
                self.assertEqual([event["event"] for event in events], ["auth"])

    def test_mismatched_or_missing_mount_version_never_extracts(self):
        for announcement in ["", ANNOUNCEMENT.replace(MANIFEST, "2"),
                             f"{ANNOUNCEMENT}\n{ANNOUNCEMENT.replace(MANIFEST, '2')}",
                             f"{ANNOUNCEMENT}\n{ANNOUNCEMENT.replace(MANIFEST, 'invalid')}"]:
            with self.subTest(announcement=announcement):
                status, events, output = self.run_preview(announcement)
                self.assertNotEqual(status, 0)
                self.assertNotIn("extract", [event["event"] for event in events])
                self.assertIn("does not match", output)

    def test_mount_failure_and_readiness_timeout_clean_up(self):
        for setting in [{"MOCK_MOUNT_EXIT": "7"}, {"MOCK_READY": "false"}]:
            with self.subTest(setting=setting):
                status, events, _ = self.run_preview(**setting)
                self.assertNotEqual(status, 0)
                self.assertNotIn("extract", [event["event"] for event in events])
                self.assertEqual(sum(event["event"] == "cleanup" for event in events), 1)

    def test_extractor_failure_preserves_exit_status_and_cleans_mount(self):
        status, events, _ = self.run_preview(MOCK_EXTRACT_EXIT="23")
        self.assertEqual(status, 23)
        self.assertEqual([event["event"] for event in events][-2:], ["cleanup", "mount-stopped"])

    def test_cleanup_failure_prevents_publication_and_never_traverses_live_mount(self):
        status, events, output = self.run_preview(MOCK_UNMOUNT_FAIL="true")
        self.assertEqual(status, 1)
        self.assertEqual([event["event"] for event in events][-2:], ["cleanup", "mount-stopped"])
        self.assertIn("Steam mount cleanup failed", output)


spec = importlib.util.spec_from_file_location("steam_installer", ROOT / "scripts/steam/install.py")
installer = importlib.util.module_from_spec(spec)
spec.loader.exec_module(installer)


class InstallerTests(unittest.TestCase):
    def make_archive(self, directory, name, *, kind=tarfile.REGTYPE, data=b"executable"):
        archive = directory / "test.tar.gz"
        with tarfile.open(archive, "w:gz") as package:
            entry = tarfile.TarInfo(name)
            entry.type = kind
            entry.size = len(data) if kind == tarfile.REGTYPE else 0
            entry.linkname = "/tmp/outside"
            package.addfile(entry, io.BytesIO(data) if entry.size else None)
        return archive

    def test_unpack_preserves_regular_release_files(self):
        with tempfile.TemporaryDirectory() as directory:
            temp = Path(directory)
            archive = self.make_archive(temp, f"{installer.RELEASE}/SteamDepotFs")
            installer.unpack(archive, temp / "output")
            self.assertEqual((temp / "output/SteamDepotFs").read_bytes(), b"executable")

    def test_unpack_rejects_path_traversal_and_links(self):
        cases = [("../outside", tarfile.REGTYPE), ("/tmp/outside", tarfile.REGTYPE),
                 (f"{installer.RELEASE}/../outside", tarfile.REGTYPE),
                 (f"{installer.RELEASE}/symlink", tarfile.SYMTYPE), (f"{installer.RELEASE}/hardlink", tarfile.LNKTYPE)]
        for name, kind in cases:
            with self.subTest(name=name), tempfile.TemporaryDirectory() as directory:
                temp = Path(directory)
                with self.assertRaises(ValueError):
                    installer.unpack(self.make_archive(temp, name, kind=kind), temp / "output")

    def test_checksum_failure_installs_nothing(self):
        with tempfile.TemporaryDirectory() as directory:
            destination = Path(directory) / "install"
            with patch.object(installer.urllib.request, "urlopen", return_value=io.BytesIO(b"corrupt")):
                with self.assertRaisesRegex(ValueError, "checksum"):
                    installer.install(destination)
            self.assertFalse(destination.exists())

    def test_existing_install_is_never_trusted_or_replaced(self):
        with tempfile.TemporaryDirectory() as directory:
            with patch.object(installer.urllib.request, "urlopen") as download:
                with self.assertRaises(ValueError):
                    installer.install(Path(directory))
                download.assert_not_called()


if __name__ == "__main__":
    unittest.main()
