#!/usr/bin/env python3
"""Plan a Steam snapshot using public version data and successful publication state."""
import argparse
import base64
from datetime import datetime, timedelta, timezone
import json
import os
from pathlib import Path
import re
import subprocess
import sys
import urllib.request

REPOSITORY = "raidertool/asset-index"
WORKFLOW = "extract.yml"
EXTRACTION_JOB = "Extract validated snapshot"
SHA = re.compile(r"[0-9a-f]{40}\Z")
TAG = re.compile(r"refs/tags/exfil-v(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\Z")
COOLDOWN = timedelta(hours=1)


def manifest_id(value):
    if not isinstance(value, str) or not re.fullmatch(r"[1-9][0-9]{0,19}", value):
        raise ValueError("Steam manifest must be a canonical positive uint64 string.")
    if int(value) > 2**64 - 1:
        raise ValueError("Steam manifest exceeds uint64.")
    return value


def steam_manifest(payload):
    depot = payload["data"]["1808500"]["depots"]["1808501"]
    public = depot["manifests"]["public"]
    return manifest_id(public["gid"] if isinstance(public, dict) else public)


def published_manifest(metadata):
    if "formatVersion" not in metadata:
        return None  # The legacy format has no validated publication marker.
    steam = metadata["steam"]
    if (metadata["formatVersion"] != 2 or steam["appId"] != 1808500
            or steam["depotId"] != 1808501):
        raise ValueError("Published metadata describes an unsupported snapshot.")
    if not SHA.fullmatch(metadata["extractorCommit"]):
        raise ValueError("Published metadata has invalid source provenance.")
    if not re.fullmatch(r"[0-9a-f]{64}", metadata["contentSha256"]):
        raise ValueError("Published metadata has an invalid digest.")
    return manifest_id(steam["manifestId"])


def github(path):
    result = subprocess.run(["gh", "api", "repos/" + REPOSITORY + "/" + path],
                            capture_output=True, text=True, timeout=45, check=False)
    if result.returncode:
        raise RuntimeError("GitHub state could not be read; no extraction was started.")
    return json.loads(result.stdout)


def public_steam_manifest():
    request = urllib.request.Request("https://api.steamcmd.net/v1/info/1808500",
                                     headers={"User-Agent": "asset-index-update"})
    with urllib.request.urlopen(request, timeout=30) as response:
        return steam_manifest(json.load(response))


def publication():
    # Read one immutable tree so metadata and its blob identity cannot disagree.
    commit = github("git/ref/heads/data")["object"]["sha"]
    if not SHA.fullmatch(commit):
        raise ValueError("Cannot resolve the data branch.")
    tree = github("git/trees/" + commit)
    entries = [entry for entry in tree["tree"] if entry["path"] == "metadata.json"]
    if not entries:
        return None, "missing"
    entry, = entries
    if entry["type"] != "blob" or entry["mode"] != "100644" or not SHA.fullmatch(entry["sha"]):
        raise ValueError("Publication metadata must be an ordinary file.")
    blob = github("git/blobs/" + entry["sha"])
    if blob["encoding"] != "base64":
        raise ValueError("Unsupported publication metadata encoding.")
    metadata = json.loads(base64.b64decode(blob["content"]))
    return published_manifest(metadata), entry["sha"]


def source_release(refs):
    releases = [(tuple(map(int, match.groups())), ref)
                for ref in refs if (match := TAG.fullmatch(ref["ref"]))]
    if not releases:
        raise ValueError("No exfil-v source release exists; release tested source first.")
    _, latest = max(releases, key=lambda item: item[0])
    source = latest["object"]
    if source["type"] != "commit" or not SHA.fullmatch(source["sha"]):
        raise ValueError("Source releases must be immutable lightweight commit tags.")
    return source["sha"], latest["ref"].removeprefix("refs/tags/")


def recent_runs(now):
    cutoff = now - COOLDOWN
    # No-op polls must never extend the cooldown. Query jobs only for actual failures.
    for page in range(1, 11):
        runs = github(f"actions/workflows/{WORKFLOW}/runs?branch=main&per_page=100&page={page}")["workflow_runs"]
        for run in runs:
            if int(run["id"]) == int(os.environ["GITHUB_RUN_ID"]):
                continue
            updated = datetime.fromisoformat(run["updated_at"].replace("Z", "+00:00"))
            if run["status"] != "completed" or updated >= cutoff:
                yield run
        if not runs or all(datetime.fromisoformat(run["created_at"].replace("Z", "+00:00")) < cutoff for run in runs):
            return
    raise RuntimeError("Too many recent runs to establish retry state safely.")


def extraction_completed_since(jobs, cutoff):
    for job in jobs:
        if job["name"] != EXTRACTION_JOB or job["conclusion"] == "skipped" or not job.get("completed_at"):
            continue
        completed = datetime.fromisoformat(job["completed_at"].replace("Z", "+00:00"))
        if completed >= cutoff:
            return True
    return False


def retry_blocked(runs, jobs, now):
    for run in runs:
        if run["status"] in {"in_progress", "waiting", "pending", "requested"}:
            return "Another update run is active."
        if run["status"] != "completed" or run["conclusion"] in {"success", "skipped"}:
            continue
        if extraction_completed_since(jobs(run["id"]), now - COOLDOWN):
            return "A failed update is within its one-hour retry cooldown."
    return None


def plan(force, enabled, event):
    if event not in {"schedule", "workflow_dispatch"}:
        raise ValueError("Unsupported update event.")
    if not enabled and event == "schedule":
        return {"needed": False, "reason": "Automatic updates are not enabled."}
    latest = public_steam_manifest()
    current, blob = publication()
    if latest == current and not force:
        return {"needed": False, "reason": "Latest Steam release is already published."}
    now = datetime.now(timezone.utc)
    blocked = retry_blocked(recent_runs(now),
                            lambda run: github(f"actions/runs/{run}/jobs?per_page=100")["jobs"], now)
    if blocked:
        return {"needed": False, "reason": blocked}
    commit, tag = source_release(github("git/matching-refs/tags/exfil-v"))
    if github(f"compare/{commit}...main")["status"] not in {"ahead", "identical"}:
        raise ValueError("Selected source release is outside main history.")
    return {"needed": True, "manifest_id": latest, "extractor_commit": commit,
            "extractor_tag": tag, "expected_metadata_blob": blob,
            "reason": "Extract the selected Steam release."}


def verify_current(manifest):
    if public_steam_manifest() != manifest_id(manifest):
        raise ValueError("Steam updated during extraction; the next check will extract the newer release.")
    # The publisher checks the previous metadata blob after fetching data. It also
    # recognizes a completed identical push whose acknowledgement was lost.


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("operation", choices=["plan", "current"])
    args = parser.parse_args()
    if os.environ.get("GITHUB_REPOSITORY") != REPOSITORY or os.environ.get("GITHUB_REF") != "refs/heads/main":
        raise ValueError("Live updates require this repository's main branch.")
    if args.operation == "current":
        verify_current(os.environ["MANIFEST_ID"])
        return
    result = plan(os.environ.get("FORCE") == "true", os.environ.get("UPDATES_ENABLED") == "true",
                  os.environ["GITHUB_EVENT_NAME"])
    with Path(os.environ["GITHUB_OUTPUT"]).open("a", encoding="utf-8") as output:
        for key, value in result.items():
            output.write(f"{key}={str(value).lower() if isinstance(value, bool) else value}\n")
    print(result["reason"])


if __name__ == "__main__":
    try:
        main()
    except Exception:
        # Do not echo service response bodies or credential-bearing command errors.
        print("Update state could not be verified; no publication was attempted.", file=sys.stderr)
        sys.exit(1)
