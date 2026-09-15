# ARC Raiders Asset Index

Extract game asset IDs, localized text, and images with
[upstream CUE4Parse](https://github.com/FabianFG/CUE4Parse).

**Preview:** coverage and the JSON format are still under review. Runs write
local files; publication and database import are not enabled.
The root CSVs, [images](images/), [metadata](metadata.json), and
[schema](schema.json) describe the existing published dataset.

## Run locally

Install the .NET SDK specified in [global.json](global.json). From this checkout:

```sh
git submodule update --init --recursive
CUE4PARSE_SKIP_NATIVE=true dotnet run --project src/AssetIndex -c Release -- \
  --game-dir "/path/to/ARC Raiders/PioneerGame/Content/Paks" \
  --output .work/preview
```

Use a new or empty output directory. The bundled [usmap](mappings/ArcRaiders.usmap)
must match your game files; `--usmap /path/to/new.usmap` selects another.
`ARC_AES_KEY` overrides the default decryption key.

To avoid a full installation, mount the depot with
[SteamDepotFS](https://github.com/raidertool/SteamDepotFS#usage) using your own
Steam credentials. Pass its mounted `PioneerGame/Content/Paks` as `--game-dir`.
See [CONTRIBUTING.md](CONTRIBUTING.md) for tests and mapping updates.

## Output

| Path | Contents |
| --- | --- |
| `assets.json` | IDs, definitions, UI metadata, localized text, and image references |
| `images/` | Decoded PNGs |
| `coverage.json` | Counts and extraction diagnostics |

IDs are decimal strings to preserve signed 64-bit values in JavaScript. Each ID
can have multiple definitions and images. Unsupported material icons remain
explicitly marked; missing text is not filled from an older snapshot.

Exit codes: `0` succeeded, `1` incomplete, `2` invalid input. Inspect coverage
and affected images even after success: success does not prove completeness.
Partial output is diagnostic evidence. Retry into a new directory under `.work/`.

## Manual preview

The [Extract preview](.github/workflows/extract.yml) workflow is manual and uses
an [unreleased SteamDepotFS auth change](https://github.com/raidertool/SteamDepotFS/pull/3).
Maintainer review is pending; complete it before configuring credentials or running it.

After review, set Actions secrets `STEAM_USERNAME` plus `STEAM_PASSWORD` or
`STEAM_ACCESS_TOKEN` (a Steam client refresh token). Authentication must work
without an interactive Guard prompt. Use **Actions → Extract preview → Run workflow**
on the reviewed branch. PR tests need no Steam credentials.

Download `asset-index-preview-<run ID>` from the run; artifacts last seven days.
Failed runs also upload available output. Credentials, Steam logs, and depot
caches are excluded. Keep the run URL: its summary records the extractor commit
and depot manifest; the downloaded JSON does not yet record them.

## Versioning

| What | Current identity |
| --- | --- |
| Published dataset | `metadata.json`: Steam build/manifest and `arc-<build>-exfil-v<version>` tag |
| Published dataset format | `schema.json` version 4; does not describe preview JSON |
| Preview code and bundled mapping | Repository commit; no extractor release or stable JSON version yet |
| Dependencies | CUE4Parse submodule commit, SDK in `global.json`, SteamDepotFS commit in the workflow |

For publication, use `main` for source and `data` for snapshots. This split is
pending; the current importer still follows `main`.

- Extractor releases: `vX.Y.Z` tags in **this repository**. The old `exfil-v…`
  component identifies the legacy producer, not this extractor.
- Snapshots: the immutable `data` commit; optional `arc-<build>-<dataShortSha>`
  tags for browsing. Existing historical tags stay unchanged.
- Snapshot metadata: extractor commit, Steam app/depot/manifest, and JSON format
  version. The source commit also identifies the bundled mapping.
- Importer: use the data commit, so source-only edits create no database version.
  Its private normalization version stays separate from the public file format.

These are publication conventions; no extractor release or data branch has
been created. Source-only changes with identical output should create no snapshot.

## Layout

```text
src/AssetIndex/          Extractor
tests/AssetIndex.Tests/  Offline tests
mappings/               Game property mapping
vendor/CUE4Parse/        Pinned upstream dependency
```
