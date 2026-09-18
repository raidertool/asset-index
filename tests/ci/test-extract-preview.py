#!/usr/bin/env python3
"""Execute the preview workflow's shell with offline Steam and FUSE substitutes."""
import json
import os
from pathlib import Path
import signal
import subprocess
import sys
import tempfile
import textwrap
import unittest

ROOT = Path(__file__).resolve().parents[2]
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

def stop_mount(*_):
    record("mount-stopped")
    sys.exit(0)

if command == "timeout":
    os.execvp(args[2], args[2:])
elif command == "sleep":
    time.sleep(0.01)
elif command == "mountpoint":
    sys.exit(0 if (temp / "ready").exists() else 1)
elif command == "fusermount3":
    record("cleanup")
    (temp / "ready").unlink(missing_ok=True)
elif command == "dotnet":
    credentials = sorted(key for key in os.environ if key.startswith("STEAM_"))
    if args[0] == "run":
        record("extract", credentials=credentials)
        sys.exit(int(os.environ["MOCK_EXTRACT_EXIT"]))
    record("steam", args=args[1:], credentials=credentials)
    if args[1] != "mount":
        sys.exit("Unexpected second Steam client")
    signal.signal(signal.SIGTERM, stop_mount)
    print(os.environ["MOCK_ANNOUNCEMENT"], file=sys.stderr, flush=True)
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


def workflow_shell():
    workflow = (ROOT / ".github/workflows/extract.yml").read_text()
    step = workflow.split("      - name: Read Steam depot and extract preview\n", 1)[1]
    script = step.split("        run: |\n", 1)[1].split("\n      - name:", 1)[0]
    return textwrap.dedent(script)


class PreviewTests(unittest.TestCase):
    def run_preview(self, announcement=ANNOUNCEMENT, *, mount_exit=0, extract_exit=0, ready=True):
        with tempfile.TemporaryDirectory() as directory:
            temp = Path(directory)
            binaries = temp / "bin"
            binaries.mkdir()
            for name in ["dotnet", "timeout", "mountpoint", "fusermount3", "sleep"]:
                mock = binaries / name
                mock.write_text(f"#!{sys.executable}\n" + MOCK)
                mock.chmod(0o755)
            env = {
                **os.environ,
                "PATH": f"{binaries}{os.pathsep}{os.environ['PATH']}",
                "RUNNER_TEMP": directory,
                "GITHUB_WORKSPACE": str(ROOT),
                "GITHUB_SHA": "1" * 40,
                "GITHUB_STEP_SUMMARY": str(temp / "summary"),
                "STEAM_USERNAME": "test-user",
                "STEAM_PASSWORD": "private-dummy-password",
                "STEAM_ACCESS_TOKEN": "private-dummy-token",
                "MOCK_ANNOUNCEMENT": announcement,
                "MOCK_MOUNT_EXIT": str(mount_exit),
                "MOCK_EXTRACT_EXIT": str(extract_exit),
                "MOCK_READY": str(ready).lower(),
            }
            process = subprocess.Popen(["bash", "-c", workflow_shell()], env=env, text=True,
                                       stdout=subprocess.PIPE, stderr=subprocess.PIPE, start_new_session=True)
            try:
                stdout, stderr = process.communicate(timeout=10)
            finally:
                # This group belongs only to this test; never leave a failed mock mount running.
                try:
                    os.killpg(process.pid, signal.SIGKILL)
                except ProcessLookupError:
                    pass
                process.wait()
            events = [json.loads(line) for line in (temp / "events.jsonl").read_text().splitlines()]
            if mount_exit == 0:
                self.assertEqual(events.pop(), {"event": "mount-stopped"})
            steam = [event for event in events if event["event"] == "steam"]
            self.assertEqual(len(steam), 1)
            self.assertEqual(steam[0]["args"][0], "mount")
            self.assertNotIn("--manifest", steam[0]["args"])
            self.assertEqual(steam[0]["credentials"], ["STEAM_ACCESS_TOKEN", "STEAM_PASSWORD", "STEAM_USERNAME"])
            self.assertEqual(sum(event["event"] == "cleanup" for event in events), 1)
            self.assertNotIn("private-dummy", stdout + stderr)
            summary = temp / "summary"
            return process.returncode, events, summary.read_text() if summary.exists() else "", stdout + stderr

    def test_one_client_records_the_mounted_manifest_before_extraction(self):
        for manifest in [MANIFEST, "1", "18446744073709551615"]:
            with self.subTest(manifest=manifest):
                announcement = ANNOUNCEMENT.replace(MANIFEST, manifest)
                status, events, summary, _ = self.run_preview(announcement)
                self.assertEqual(status, 0)
                self.assertEqual([event["event"] for event in events], ["steam", "extract", "cleanup"])
                self.assertEqual(events[1]["credentials"], [])
                self.assertEqual(summary, f"Extractor commit: {'1' * 40}\nSteam app: 1808500; depot: 1808501; manifest: {manifest}\n")

    def test_identical_repeated_announcements_are_consistent(self):
        status, _, summary, _ = self.run_preview(f"{ANNOUNCEMENT}\n{ANNOUNCEMENT}")
        self.assertEqual(status, 0)
        self.assertEqual(summary.count(MANIFEST), 1)

    def test_missing_invalid_or_conflicting_manifest_never_extracts(self):
        invalid = ["", "0", "01", "-1", "18446744073709551616", "100000000000000000000", "abc",
                   "$(echo private-dummy)", "1 2"]
        announcements = [ANNOUNCEMENT.replace(MANIFEST, value) for value in invalid]
        announcements += ["", f"prefix {ANNOUNCEMENT}", ANNOUNCEMENT.replace("app=1808500", "app=1"),
                          ANNOUNCEMENT.replace("depot=1808501", "depot=1"),
                          f"{ANNOUNCEMENT}\n{ANNOUNCEMENT.replace(MANIFEST, '2')}"]
        for announcement in announcements:
            with self.subTest(announcement=announcement):
                status, events, summary, output = self.run_preview(announcement)
                self.assertNotEqual(status, 0)
                self.assertEqual([event["event"] for event in events], ["steam", "cleanup"])
                self.assertEqual(summary, "")
                self.assertIn("did not announce one valid mounted manifest", output)

    def test_failed_mount_never_extracts_or_discloses_raw_log(self):
        log = f"{ANNOUNCEMENT}\nSteam logon failed: AccessDenied\nprivate-dummy-token"
        status, events, summary, output = self.run_preview(log, mount_exit=7)
        self.assertNotEqual(status, 0)
        self.assertEqual([event["event"] for event in events], ["steam", "cleanup"])
        self.assertEqual(summary, "")
        self.assertIn("Steam mount failed (exit 7)", output)

    def test_manifest_is_not_recorded_without_mount_readiness(self):
        status, events, summary, _ = self.run_preview(ready=False)
        self.assertNotEqual(status, 0)
        self.assertEqual([event["event"] for event in events], ["steam", "cleanup"])
        self.assertEqual(summary, "")

    def test_extractor_failure_keeps_exit_status_and_cleans_mount(self):
        status, events, _, _ = self.run_preview(extract_exit=23)
        self.assertEqual(status, 23)
        self.assertEqual([event["event"] for event in events], ["steam", "extract", "cleanup"])


if __name__ == "__main__":
    unittest.main()
