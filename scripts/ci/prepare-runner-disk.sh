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
sudo rm -rf -- /usr/local/lib/android /usr/local/.ghcup \
  /opt/hostedtoolcache/CodeQL /usr/share/swift /usr/lib/jvm \
  /opt/az /etc/skel/.rustup /home/runner/.rustup

# SteamDepotFS streams selected pages. Report capacity; the command monitor
# enforces the operational free-space reserve while extraction/export run.
available_kib="$(df -Pk -- "$RUNNER_TEMP" | awk 'NR == 2 {print $4}')"
if [[ ! "$available_kib" =~ ^[0-9]+$ ]]; then
  echo "Could not read available runner disk space." >&2
  exit 1
fi
printf 'Extraction disk after cleanup: %s KiB available.\n' "$available_kib"
