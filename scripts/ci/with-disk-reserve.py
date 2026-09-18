#!/usr/bin/env python3
"""Run one command with a sampled disk reserve and aggregate usage reporting."""
import argparse
from contextlib import nullcontext
import os
from pathlib import Path
import shutil
import signal
import subprocess
import sys
import time

GIB = 1024 ** 3
RESERVE = 2 * GIB
INTERVAL = 5


def stop_group(process):
    """Allow shell cleanup to finish, then reap only this command's children."""
    deadline = time.monotonic() + 30
    try:
        os.killpg(process.pid, signal.SIGTERM)
    except ProcessLookupError:
        return
    try:
        process.wait(timeout=30)
    except subprocess.TimeoutExpired:
        pass
    # A shell may finish before its children; they still share its group.
    while time.monotonic() < deadline:
        try:
            os.killpg(process.pid, 0)
        except ProcessLookupError:
            return
        time.sleep(0.1)
    try:
        os.killpg(process.pid, signal.SIGKILL)
    except ProcessLookupError:
        pass
    process.wait(timeout=5)


def run(command, directory, output):
    initial = minimum = shutil.disk_usage(directory).free
    process = None
    print(f"Disk reserve: {RESERVE / GIB:.2f} GiB; starting free: {initial / GIB:.2f} GiB.", flush=True)
    try:
        if initial < RESERVE:
            raise RuntimeError("Disk reserve unavailable; command was not started.")
        process = subprocess.Popen(command, stdout=output, stderr=output, start_new_session=True,
                                   env={**os.environ, "TMPDIR": str(directory)})
        while True:
            minimum = min(minimum, shutil.disk_usage(directory).free)
            if minimum < RESERVE:
                raise RuntimeError("Disk reserve reached; stopping work without publishing.")
            result = process.poll()
            if result is not None:
                return result if result >= 0 else 128 - result
            time.sleep(INTERVAL)
    finally:
        if process is not None:
            stop_group(process)
        print(f"Sampled disk usage: minimum free {minimum / GIB:.2f} GiB; "
              f"peak added usage {max(0, initial - minimum) / GIB:.2f} GiB "
              f"({INTERVAL}s samples).", flush=True)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--log", type=Path, help="Keep the child command's output in a local log.")
    parser.add_argument("command", nargs=argparse.REMAINDER)
    args = parser.parse_args()
    command = args.command[1:] if args.command[:1] == ["--"] else args.command
    directory = Path(os.environ.get("RUNNER_TEMP", ""))
    if not command or not directory.is_absolute() or not directory.is_dir():
        raise RuntimeError("A command and an existing absolute runner temporary directory are required.")
    for signum in (signal.SIGINT, signal.SIGTERM):
        signal.signal(signum, lambda received, _frame: sys.exit(128 + received))
    with args.log.open("w") if args.log is not None else nullcontext() as output:
        return run(command, directory, output)


if __name__ == "__main__":
    try:
        sys.exit(main())
    except RuntimeError as error:
        print(str(error), file=sys.stderr)
        sys.exit(1)
    except (OSError, subprocess.SubprocessError):
        print("Disk monitoring or command execution failed; no data was published.", file=sys.stderr)
        sys.exit(1)
