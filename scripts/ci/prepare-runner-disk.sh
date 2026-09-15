#!/usr/bin/env bash
set -euo pipefail

if [[ "${GITHUB_ACTIONS:-}" != true || "${RUNNER_ENVIRONMENT:-}" != github-hosted ||
      "${RUNNER_OS:-}" != Linux || "$(uname -s)" != Linux ]]; then
  echo "Disk preparation requires a disposable GitHub-hosted Linux runner." >&2
  exit 1
fi
if [[ "${RUNNER_TEMP:-}" != /* || ! -d "$RUNNER_TEMP" ]]; then
  echo "RUNNER_TEMP must name an existing absolute directory." >&2
  exit 1
fi

# Unused preinstalled SDKs only. Keep .NET and the rest of the tool cache.
sudo rm -rf -- /usr/local/lib/android /usr/local/.ghcup /opt/hostedtoolcache/CodeQL

# Current build: ~19 GiB raw packages + 8 GiB Steam cache + guard/output space.
available_kib="$(df -Pk -- "$RUNNER_TEMP" | awk 'NR == 2 {print $4}')"
if [[ ! "$available_kib" =~ ^[0-9]+$ ]]; then
  echo "Could not read available runner disk space." >&2
  exit 1
fi
required_kib=$((32 * 1024 * 1024))
printf 'Extraction disk: %s KiB available; %s KiB required.\n' "$available_kib" "$required_kib"
if (( available_kib < required_kib )); then
  echo "Extraction requires at least 32 GiB free for package spool, Steam cache and output." >&2
  exit 1
fi
