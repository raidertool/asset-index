# Contributing

Open code, mapping and workflow PRs against `main`. Dataset usage documentation
lives in the `data` branch README; update it alongside format changes. The
publisher preserves that README and replaces only generated files.

## Test

Initialize submodules as described in the [README](README.md#run-locally), then run
the tests relevant to your change:

```sh
CUE4PARSE_SKIP_NATIVE=true dotnet test tests/AssetIndex.Tests/AssetIndex.Tests.csproj -c Release
dotnet test tests/PublishSnapshot.Tests/PublishSnapshot.Tests.csproj -c Release
python3 tests/ci/test-release.py
python3 tests/ci/test-update.py
python3 tests/ci/test-extract-preview.py
python3 tests/ci/test-disk-reserve.py
bash tests/ci/test-prepare-runner-disk.sh
```

PR tests are offline and require no Steam credentials. Publisher tests use
local Git repositories. For live checks, use your installed game or your own
Steam credentials. Keep credentials, game files and complete previews out of Git
and issues; state which game build you tested.

## Extraction and mappings

Use `--usmap /path/to/other.usmap` to select another mapping; `ARC_AES_KEY`
overrides the default key. Exit codes: `0` succeeded, `1` incomplete, `2` invalid
input. Inspect `coverage.json` and affected records/images even after success.
It reports extraction counts and limitations, not a percentage of the whole game.
Retry incomplete extraction into a new directory.

For coverage fixes, verify the game field/reference that owns the value and add
a small synthetic regression, including empty or conflicting values. Do not
infer labels from filenames or restore output solely because an old snapshot had it.

Replace `mappings/ArcRaiders.usmap` by PR, stating its source and tested game
build or marking compatibility untested. Keep CUE4Parse pinned upstream and run
the relevant tests for mapping/dependency changes.

## Releases

Use Conventional Commits: `feat` bumps minor, `fix` patch, and `!` or a
`BREAKING CHANGE:` footer major. After successful main CI, relevant source,
mapping and runtime changes create an immutable `exfil-vX.Y.Z` tag. Documentation,
tests and generated output do not bump software. Preview with
`python3 scripts/release/version.py`.

Data has separate `arc-<SteamManifestId>-<contentSha25612>` tags. A new Steam
release gets a snapshot identity even when the payload is unchanged. Software
releases alone do not extract or publish data.

## Run an update

Open [Update asset snapshot](https://github.com/raidertool/asset-index/actions/workflows/extract.yml)
and select **Run workflow** on `main`, or use:

```sh
gh workflow run extract.yml --repo raidertool/asset-index --ref main \
  -f force=true -f publish=false
```

`force` re-extracts an already published Steam release. It does not bypass the
one-hour failure cooldown. `publish=false` still uploads a publicly readable
validated export, retained for one day; `publish=true` also updates `data` and its
snapshot tag atomically. Game files, authentication logs and full discovery
records are excluded. Let an active update finish before dispatching another.

Maintainer setup:

1. Restrict `steam-extraction` and `asset-publication` environments to `main`.
2. Store `STEAM_USERNAME` and `STEAM_PASSWORD` only in `steam-extraction`.
3. Protect source updates through PRs; permit the publisher's `GITHUB_TOKEN` to
   fast-forward `data` and create immutable snapshot tags. The data branch must exist.
4. Verify a manual publication, then set `ASSET_UPDATES_ENABLED=true` for scheduled
   updates. Manual runs work while that flag is unset.

The schedule checks every five minutes and extracts only unpublished Steam
releases using the latest tested source release. GitHub may delay or skip checks;
schedules can be disabled after 60 days without repository activity. Failed
attempts have a one-hour cooldown before retry.

For local publication and the validation contract, see
[implementation notes](docs/notes.md#local-publication).
