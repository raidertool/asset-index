#!/usr/bin/env bash
set -euo pipefail

repo_root="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")/../.." && pwd)"
test_dir="$(mktemp -d)"
trap 'rm -rf -- "$test_dir"' EXIT
mkdir "$test_dir/bin"
export DISK_TEST_LOG="$test_dir/calls"
cat > "$test_dir/bin/sudo" <<'MOCK'
#!/usr/bin/env bash
printf '%s\n' "$@" > "$DISK_TEST_LOG"
exit "${MOCK_SUDO_RESULT:-0}"
MOCK
cat > "$test_dir/bin/df" <<'MOCK'
#!/usr/bin/env bash
printf 'Filesystem 1024-blocks Used Available Capacity Mounted\n'
printf '/dev/fake 99999999 0 %s 0%% /\n' "${MOCK_AVAILABLE_KIB:-33554432}"
exit "${MOCK_DF_RESULT:-0}"
MOCK
cat > "$test_dir/bin/uname" <<'MOCK'
#!/usr/bin/env bash
printf '%s\n' "${MOCK_KERNEL:-Linux}"
MOCK
chmod +x "$test_dir/bin/"*
export PATH="$test_dir/bin:$PATH"
export GITHUB_ACTIONS=true RUNNER_OS=Linux RUNNER_ENVIRONMENT=github-hosted RUNNER_TEMP="$test_dir"
script="$repo_root/scripts/ci/prepare-runner-disk.sh"

expect_failure() {
  if env "$@" bash "$script" > "$test_dir/output" 2>&1; then
    echo "Expected disk preparation to fail: $*" >&2
    exit 1
  fi
}

for setting in GITHUB_ACTIONS=false RUNNER_OS=macOS RUNNER_ENVIRONMENT=self-hosted MOCK_KERNEL=Darwin RUNNER_TEMP=relative; do
  expect_failure "$setting"
  [[ ! -e "$DISK_TEST_LOG" ]] || { echo "Guard allowed cleanup: $setting" >&2; exit 1; }
done
expect_failure MOCK_SUDO_RESULT=17
expect_failure MOCK_DF_RESULT=18
expect_failure MOCK_AVAILABLE_KIB=unknown
expect_failure MOCK_AVAILABLE_KIB=33554431
bash "$script"
cat > "$test_dir/expected" <<'EXPECTED'
rm
-rf
--
/usr/local/lib/android
/usr/local/.ghcup
/opt/hostedtoolcache/CodeQL
/usr/share/swift
/usr/lib/jvm
/opt/az
/etc/skel/.rustup
/home/runner/.rustup
EXPECTED
diff -u "$test_dir/expected" "$DISK_TEST_LOG"
echo "Runner disk preparation: 10 mocked cases passed."
