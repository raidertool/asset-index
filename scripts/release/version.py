#!/usr/bin/env python3
"""Plan a source release; --publish creates only an immutable remote tag."""
import argparse
import json
from pathlib import Path
import re
import subprocess
import sys

LEGACY_TAG = "arc-24017548-exfil-v0.13.2"
LEGACY_VERSION = (0, 13, 2)
TAG = re.compile(r"exfil-v(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)$")
CONVENTIONAL = re.compile(r"([a-z][a-z0-9-]*)(?:\([^\n]+\))?(!)?: .+")
BUILD_FILES = {"global.json", "Directory.Build.props", "Directory.Build.targets",
               "Directory.Packages.props", "NuGet.config", ".gitmodules"}
RUNTIME_FILES = {".github/workflows/extract.yml", "scripts/ci/prepare-runner-disk.sh",
                 "scripts/ci/with-disk-reserve.py"}


def git(repo, *args):
    return subprocess.check_output(["git", "-C", str(repo), *args], text=True).strip()


def ancestor(repo, older, newer):
    result = subprocess.run(["git", "-C", str(repo), "merge-base", "--is-ancestor", older, newer], check=False)
    if result.returncode not in (0, 1):
        raise RuntimeError("Cannot verify release ancestry.")
    return result.returncode == 0


def source_path(path):
    if Path(path).suffix.lower() in {".md", ".rst", ".adoc"}:
        return False
    return (path.startswith(("src/", "vendor/", "scripts/steam/", "scripts/automation/")) or path == "vendor/CUE4Parse"
            or (path.startswith("mappings/") and path.endswith(".usmap"))
            or path in BUILD_FILES or path in RUNTIME_FILES
            or ("/" not in path and path.endswith((".sln", ".slnx"))))


def severity(message):
    lines = message.splitlines()
    if not lines:
        return 0
    match = CONVENTIONAL.fullmatch(lines[0])
    if not match:
        return 0
    if match[2] or re.search(r"(?m)^BREAKING(?: CHANGE|-CHANGE):\s*\S", message):
        return 3
    return {"feat": 2, "fix": 1, "perf": 1}.get(match[1], 0)


def baseline(repo):
    versions = [(tuple(map(int, match.groups())), tag)
                for tag in git(repo, "tag", "--list", "exfil-v*").splitlines()
                if (match := TAG.fullmatch(tag))]
    if versions:
        return max(versions)
    git(repo, "rev-parse", "--verify", LEGACY_TAG + "^{commit}")
    return LEGACY_VERSION, LEGACY_TAG


def changed_paths(repo, commit):
    # NUL framing preserves Unicode and embedded whitespace without Git quoting.
    output = subprocess.check_output(
        ["git", "-C", str(repo), "show", "--format=", "--name-only", "--no-renames",
         "--diff-merges=first-parent", "-z", commit], text=True, errors="surrogateescape")
    return [path for path in output.split("\0") if path]


def release_level(repo, base, target):
    level = 0
    for commit in git(repo, "rev-list", base + ".." + target).splitlines():
        message = git(repo, "show", "-s", "--format=%B", commit)
        change = severity(message)
        if change <= level:
            continue
        if any(source_path(path) for path in changed_paths(repo, commit)):
            level = change
    return level


def plan(repo, target="HEAD"):
    target = git(repo, "rev-parse", "--verify", target + "^{commit}")
    version, base = baseline(repo)
    if base != LEGACY_TAG and not ancestor(repo, base, target):
        if ancestor(repo, target, base):
            return {"tag": None, "reason": "A newer source release already exists."}
        raise RuntimeError("Latest source release is outside the target ancestry.")
    level = release_level(repo, base, target)
    if not level:
        return {"tag": None, "reason": "No unreleased source feat, fix, perf or breaking change."}
    major, minor, patch = version
    next_version = {1: (major, minor, patch + 1),
                    2: (major, minor + 1, 0),
                    3: (major + 1, 0, 0)}[level]
    return {"tag": "exfil-v" + ".".join(map(str, next_version)), "commit": target,
            "baseTag": base, "change": ("patch", "minor", "major")[level - 1]}


def publish(repo, target, remote):
    git(repo, "fetch", "--tags", "--no-recurse-submodules", remote,
        "refs/heads/main:refs/remotes/release/main")
    target = git(repo, "rev-parse", "--verify", target + "^{commit}")
    if not ancestor(repo, target, "refs/remotes/release/main"):
        raise RuntimeError("Release target is not on remote main.")
    _, base = baseline(repo)
    if base != LEGACY_TAG and not ancestor(repo, base, "refs/remotes/release/main"):
        raise RuntimeError("Latest source release is not on remote main.")
    result = plan(repo, target)
    if result["tag"]:
        # No force: a concurrent tag creation can succeed identically or reject.
        git(repo, "push", remote, result["commit"] + ":refs/tags/" + result["tag"])
    return result


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--target", default="HEAD")
    parser.add_argument("--publish", action="store_true")
    parser.add_argument("--remote", default="origin")
    args = parser.parse_args()
    repo = Path.cwd()
    result = publish(repo, args.target, args.remote) if args.publish else plan(repo, args.target)
    print(json.dumps(result, sort_keys=True))


if __name__ == "__main__":
    try:
        main()
    except (RuntimeError, subprocess.CalledProcessError) as error:
        print(str(error) if isinstance(error, RuntimeError) else "Git release operation failed; no ref was forced.", file=sys.stderr)
        sys.exit(1)
