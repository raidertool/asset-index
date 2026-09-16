# Contributing

Keep patches small. Add abstractions when a concrete feature needs them.

## Test

Initialize submodules as shown in the [README](README.md#run-locally), then run:

```sh
CUE4PARSE_SKIP_NATIVE=true dotnet test tests/AssetIndex.Tests/AssetIndex.Tests.csproj -c Release
dotnet test tests/PublishSnapshot.Tests/PublishSnapshot.Tests.csproj -c Release
```

PR CI runs offline without game files or credentials. For live checks, use your
installed game or SteamDepotFS with your own local Steam credentials. Keep
credentials, game files, depot caches, and generated previews out of Git and issues.

Publisher tests use temporary local Git repositories. See [publication](docs/publication.md)
for the draft snapshot contract and activation requirements.

## Fix coverage

1. Identify the issue, asset ID, or game path.
2. Trace the value in game files; avoid guessed names and image associations.
3. Add a small synthetic regression test where practical.
4. Include the extractor commit, game build/depot manifest, and relevant coverage
   results. State when live testing was unavailable.

Inspect `coverage.json`, the affected JSON rows, and decoded images. Known cases
include XP, Scrappy, and IDs in the [issues](https://github.com/raidertool/asset-index/issues).
Matching the old dataset does not establish completeness.

## Update mappings or dependencies

Replace `mappings/ArcRaiders.usmap` in a PR. State its source and tested game
build, or mark compatibility untested. Git records mapping revisions; no extra
mapping metadata or schema-diff report is needed.

Keep CUE4Parse pinned to an upstream commit. For any dependency update, run the
offline tests and report relevant live coverage changes when available.

Submit source, tests, docs, or mappings. Generated files belong on `data`, not the
source branch; preview output is not a publication.
See the README for [manual runs](README.md#manual-preview) and the proposed
[versioning](README.md#versioning). Retire and drain the old publisher before
merging the source layout; it still rewrites the root metadata, schema, and README.
