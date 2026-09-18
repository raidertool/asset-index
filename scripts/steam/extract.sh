#!/usr/bin/env bash
set +x
set -euo pipefail
umask 077

fail() { echo "$1" >&2; exit 1; }

script_dir="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)"
source_dir="${SOURCE_DIR:-$(cd -- "$script_dir/../.." && pwd)}"
[[ -d "${RUNNER_TEMP:-}" ]] || fail 'Runner temporary directory is required.'
[[ -x "${STEAM_DEPOTFS:-}" ]] || fail 'The pinned SteamDepotFS installation is required.'
[[ "${EXTRACTOR_COMMIT:-}" =~ ^[0-9a-f]{40}$ ]] || fail 'A pinned extractor commit is required.'
[[ "$(git -C "$source_dir" rev-parse HEAD 2>/dev/null)" == "$EXTRACTOR_COMMIT" ]] || fail 'Extractor checkout does not match the pinned commit.'
# Bash arithmetic cannot represent the whole unsigned 64-bit range.
[[ "${MANIFEST_ID:-}" =~ ^[1-9][0-9]*$ ]] || fail 'A valid Steam manifest ID is required.'
[[ ${#MANIFEST_ID} -le 20 ]] || fail 'A valid Steam manifest ID is required.'
# shellcheck disable=SC2071
[[ ${#MANIFEST_ID} -lt 20 || "$MANIFEST_ID" < 18446744073709551616 ]] || fail 'A valid Steam manifest ID is required.'
[[ -n "${STEAM_USERNAME:-}" && -n "${STEAM_PASSWORD:-}" ]] || fail 'Steam username and password are required.'

private_dir="$(mktemp -d "$RUNNER_TEMP/steam-session.XXXXXX")"
mount_dir="$private_dir/depot"
mount_pid=''
cleanup() {
  unset STEAM_USERNAME STEAM_PASSWORD STEAM_ACCESS_TOKEN token
  if [[ -n "$mount_pid" ]]; then
    timeout --foreground --kill-after=5s 10s fusermount3 -u "$mount_dir" >/dev/null 2>&1 || true
    kill "$mount_pid" >/dev/null 2>&1 || true
    for _ in {1..5}; do
      kill -0 "$mount_pid" 2>/dev/null || break
      sleep 1
    done
    if kill -0 "$mount_pid" 2>/dev/null; then
      kill -KILL "$mount_pid" >/dev/null 2>&1 || true
    fi
    wait "$mount_pid" 2>/dev/null || true
  fi
  # Never traverse a live FUSE filesystem during cache/log cleanup.
  if ! mountpoint -q "$mount_dir"; then
    rm -rf -- "$private_dir"
  else
    echo 'Steam mount cleanup failed; the disposable runner must be discarded.' >&2
    exit 1
  fi
}
trap cleanup EXIT
trap 'exit 130' INT
trap 'exit 143' TERM

unset STEAM_ACCESS_TOKEN STEAM_AUTH_CODE STEAM_TWO_FACTOR_CODE STEAM_LOGIN_ID
echo 'Authenticating to Steam...'
if ! token="$(timeout --foreground --kill-after=5s 3m dotnet "$source_dir/src/SteamAuthToken/bin/Release/net10.0/SteamAuthToken.dll" 2>"$private_dir/auth.log")"; then
  fail 'Steam authentication failed. Verify the account locally before retrying.'
fi
[[ -n "$token" && ${#token} -le 16384 && "$token" != *$'\n'* && "$token" != *$'\r'* ]] || fail 'Steam authentication returned an invalid token.'
export STEAM_ACCESS_TOKEN="$token"
unset token STEAM_PASSWORD
mkdir -p "$mount_dir"
login_id="$(python3 -c 'import secrets; print(secrets.randbelow(2**32 - 1) + 1)')"
echo 'Mounting the requested Steam version...'
# Keep child processes in the monitored group so cancellation reaches them.
timeout --foreground --kill-after=15s 130m "$STEAM_DEPOTFS" mount \
  --app 1808500 --depot 1808501 --branch public --manifest "$MANIFEST_ID" \
  --login-id "$login_id" \
  --cache-dir "$private_dir/chunks" \
  --cache-max-bytes 8G --cache-low-watermark 6G --cache-min-free-bytes 2G \
  --timeout 7800 --mount-point "$mount_dir" >"$private_dir/mount.log" 2>&1 &
mount_pid=$!
unset STEAM_USERNAME STEAM_PASSWORD STEAM_ACCESS_TOKEN
for _ in {1..120}; do
  mountpoint -q "$mount_dir" && break
  kill -0 "$mount_pid" 2>/dev/null || fail 'Steam mount failed. Verify account access and connectivity locally.'
  sleep 1
done
mountpoint -q "$mount_dir" || fail 'Steam mount readiness timed out.'
mounted_id="$(sed -nE 's/^mounting app=1808500 depot=1808501 manifest=(.*) at .+$/\1/p' "$private_dir/mount.log" | sort -u)"
[[ "$mounted_id" == "$MANIFEST_ID" ]] || fail 'Mounted Steam version does not match the requested version.'
game_dir="$mount_dir/PioneerGame/Content/Paks"
timeout --foreground --kill-after=5s 20s test -d "$game_dir" || fail 'Game files are unavailable in the mounted version.'
echo 'Extracting game assets...'
if timeout --foreground --kill-after=15s 120m dotnet "$source_dir/src/AssetIndex/bin/Release/net10.0/AssetIndex.dll" \
  --game-dir "$game_dir" --usmap "$source_dir/mappings/ArcRaiders.usmap" \
  --output "$RUNNER_TEMP/asset-index-preview" >"$private_dir/extraction.log" 2>&1; then
  echo 'Extraction completed; full validation and public export follow.'
else
  result=$?
  echo 'Extraction failed. Reproduce locally to inspect diagnostics.' >&2
  exit "$result"
fi
