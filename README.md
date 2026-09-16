# ARC Raiders Asset Index

Extract game asset IDs, localized text, and images with
[upstream CUE4Parse](https://github.com/FabianFG/CUE4Parse).

**Preview:** coverage and the JSON format are still under review. Preview runs
write local files without publishing or importing them.
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
Allow at least 32 GiB of free temporary disk for the current build’s package spool,
Steam cache and generated output; larger builds may need more.
See [CONTRIBUTING.md](CONTRIBUTING.md) for tests and mapping updates.

## Output

| Path | Contents |
| --- | --- |
| `assets.json` | IDs, definitions, UI metadata, localized text, and image references |
| `resources.json`, `images/` | Registry UI textures and explicit asset image roles, including supported material icons |
| `discovery/` | Object fields, every package's export headers and selected-body coverage |
| `localization/` | Compressed translation dictionaries |
| `coverage.json` | Counts and extraction diagnostics |

IDs are decimal strings to preserve signed 64-bit values in JavaScript. Each ID
can have multiple definitions and images. Its `presentation` explains selected
text and alternative source fields. Each image's `resource` names its texture or
material. [Material rendering](docs/materials.md) uses explicit standalone image
conditions. Unsupported materials remain marked; missing text is not filled
from an older snapshot.
See [discovery rules](docs/discovery.md) before adding new associations.

Exit codes: `0` succeeded, `1` incomplete, `2` invalid input. Inspect coverage
and affected images even after success: success does not prove completeness.
Partial output is diagnostic evidence. Retry into a new directory under `.work/`.

## Manual preview

Run game-backed previews locally or in a private repository; keep their complete
output and logs private. The [Extract preview](.github/workflows/extract.yml)
workflow skips public repositories. Public CI runs offline tests without Steam
credentials. A safe public snapshot handoff is not implemented yet.

## Versioning

The current published CSVs use `schema.json` v4 and legacy
`arc-<build>-exfil-v<version>` tags. Preview JSON has no stable release yet;
the source commit identifies its code and bundled mapping.

The planned source/data split is not active:

| Identity | Convention |
| --- | --- |
| Extractor | Source on `main`; `vX.Y.Z` releases in this repository |
| Snapshot | Immutable `data` commit; `arc-<manifestId>-<dataCommit12>` tag |
| Metadata | Extractor commit, Steam app/depot/manifest, JSON format version |
| Database import | Data commit; private normalization version tracked separately |

Publish the data commit and tag atomically. Once the importer follows `data`,
identical output creates no snapshot or database version, provided normalization
is unchanged. Historical tags stay unchanged. No data branch or extractor release
has been created.

Today the importer follows `main` and versions by commit. Before merging source
changes or enabling publication, follow the [activation order](docs/publication.md#activation).
See [snapshot publication](docs/publication.md) for the command and validation contract.

## Layout

```text
src/AssetIndex/                Extractor
src/PublishSnapshot/           Snapshot validator and publisher (inactive)
tests/AssetIndex.Tests/        Offline extractor tests
tests/PublishSnapshot.Tests/   Offline publisher and Git atomicity tests
mappings/                     Game property mapping
vendor/CUE4Parse/              Pinned upstream dependency
```
