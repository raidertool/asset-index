# Contributing

Keep patches small and use Conventional Commits. Open code, mapping and workflow
PRs against `main`. Generated files live on `data`; publish them through the
validated publisher.

## Test

Initialize submodules as described in the [README](README.md#run-locally), then:

```sh
CUE4PARSE_SKIP_NATIVE=true dotnet test tests/AssetIndex.Tests/AssetIndex.Tests.csproj -c Release
dotnet test tests/PublishSnapshot.Tests/PublishSnapshot.Tests.csproj -c Release
python3 tests/ci/test-release.py
python3 tests/ci/test-update.py
python3 tests/ci/test-extract-preview.py
bash tests/ci/test-prepare-runner-disk.sh
```

Pull-request CI is offline. Publisher and release tests use temporary local Git repositories;
they do not contact GitHub. Use your installed game or your own Steam credentials
for live checks. Keep credentials, game files and complete previews out of Git
and issues. State when live compatibility was not tested.

## Fix coverage

1. Identify the affected ID, source field or image path.
2. Verify its typed identity and presentation relationship in current game data.
3. Add a small synthetic regression covering the relationship and empty/conflicting values.
4. Inspect the relevant JSON, CSV, images and coverage output.

Matching historical output alone does not establish correctness or completeness.
Do not guess a name or restore an image from an older snapshot.

## Update mappings or dependencies

Replace `mappings/ArcRaiders.usmap` by PR. Describe its source and tested game
build, or mark compatibility untested. Git records mapping revisions; no parallel
mapping version is needed. Keep CUE4Parse pinned to an upstream commit and run
the relevant offline tests for dependency changes.

## Software releases

Use `feat`, `fix`, or a breaking-change marker for relevant software changes.
The release script checks changed file paths rather than trusting the commit
scope. It includes `src/`, `vendor/`, `.usmap` files under `mappings/`, and root SDK,
solution and dependency configuration, plus update/Steam scripts, the extraction
workflow and runner disk-preparation script. Docs and generated output are excluded.

Preview the version locally with `python3 scripts/release/version.py`; this only
reads Git. After successful push CI on `main`, the serialized release workflow
creates an immutable `exfil-vX.Y.Z` tag. Reruns and older completed CI runs do not
create extra versions. A conflicting remote tag is never replaced.

Software tags do not publish game data. Data uses the separate manifest/content
identity and [activation requirements](README.md#versions-and-publication).
