# ARC Raiders Asset Index

Game asset IDs, localized text and images, extracted with
[upstream CUE4Parse](https://github.com/FabianFG/CUE4Parse).

**Preview:** the files below describe the new export. The root CSVs remain in
the [legacy format](https://github.com/raidertool/asset-index/tree/6a68cb6608e9d2eaf802ad239172038c6596b21f)
until the validated replacement is published. Source and generated data share `main`.

## Use the data

| File | Contents |
| --- | --- |
| `asset_index.csv` | `asset_id,asset_name,display_name,description,image,wide_image` |
| `asset_localizations.csv` | `asset_id,locale,display_name,description` |
| `assets.json` | Full definitions, presentation sources, localized text and image roles |
| `resources.json`, `images/` | All exported images, including UI textures without an asset ID |
| `localization/` | Compressed translation dictionaries |
| `metadata.json`, `coverage.json` | Snapshot identity and extraction diagnostics |

IDs are decimal strings in JSON; read CSV IDs as strings too. One ID can have
multiple definitions and image roles. The core CSV is a convenient projection;
`assets.json` retains the detail.

Localized JSON and CSV fields stay blank when the selected translation is absent.
Authored source text is separate in `presentation.name.source`,
`presentation.description.source` and candidate references. The English core CSV
uses the selected English translation, with authored source text as its explicit
fallback. Culture-invariant text applies to every locale.

Image paths preserve the original texture name and package directories. For
example, `/Game/Pioneer/UI/Icon.Icon` becomes `images/Game/Pioneer/UI/Icon.png`.
Use the published reference; several assets can share one image. `wide_image`
contains a supported whole-asset image that is wider than it is tall, when one
exists. A map region or control icon does not become an item's wide image.

## Run locally

Install the SDK in [global.json](global.json), then:

```sh
git submodule update --init --recursive
CUE4PARSE_SKIP_NATIVE=true dotnet run --project src/AssetIndex -c Release -- \
  --game-dir "/path/to/ARC Raiders/PioneerGame/Content/Paks" \
  --output .work/preview
```

Use a new or empty output directory. The bundled
[mapping](mappings/README.md) must match your game files;
`--usmap /path/to/other.usmap` selects another. `ARC_AES_KEY` overrides the default
key. [SteamDepotFS](https://github.com/raidertool/SteamDepotFS#usage) can supply the
same game directory using your own Steam credentials. Allow at least 32 GiB of
free temporary disk for the current build; larger builds may need more.

Exit codes: `0` succeeded, `1` incomplete, `2` invalid input. Inspect coverage
and affected rows/images even after success. Partial output is diagnostic evidence;
retry into a new directory. Full `discovery/` evidence remains in the preview.

Game-backed previews run locally or in a private repository. The
[manual workflow](.github/workflows/extract.yml) skips extraction and artifact
upload in public repositories. Public CI runs offline without Steam credentials.
A successful preview does not publish itself.

## Versions and publication

| Identity | Convention |
| --- | --- |
| Software | Immutable `exfil-vX.Y.Z` tag on a tested source commit |
| Data | Immutable `arc-<SteamManifestId>-<contentSha25612>` tag on a snapshot commit |
| Snapshot metadata | Format version, full content hash, extractor commit and Steam identifiers |

After successful main-branch CI, software tags are calculated from Conventional
Commits: `feat` increments minor, `fix` patch, and `!` or a `BREAKING CHANGE:`
footer major. Only commits changing extractor/publisher source, mappings,
dependencies or build inputs count. Generated-data, documentation, tests and
release-tooling-only commits do not bump software. The first new tag continues
from legacy version `0.13.2`; existing `arc-...-exfil-v...` tags remain unchanged.

The publisher validates a complete preview before updating generated files on
`main`, preserving source and docs:

```sh
dotnet run --project src/PublishSnapshot -c Release -- \
  <preview-directory> <remote> <extractor-commit> <Steam-manifest-id>
```

The data digest covers the sorted generated Git blob inventory; it excludes
metadata, coverage and source. The same manifest and content create no new
snapshot even after source-only commits. A new Steam manifest creates a snapshot
identity even when its content is unchanged. Main and the data tag are pushed
atomically without overwriting a concurrent writer.

`--export <preview> <new-directory> <extractor-commit> <Steam-manifest-id>`
performs the same validation and writes only public files locally. Full discovery
evidence and diagnostic messages are excluded.

Scheduling and importer cutover remain pending a game-backed rehearsal. The
existing dataset remains available until its validated replacement is ready.

See [CONTRIBUTING.md](CONTRIBUTING.md) for tests and mapping updates, and
[implementation notes](docs/notes.md) for selection and validation rules.
