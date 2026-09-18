# ARC Raiders Asset Index

Game asset IDs, localized text and images, extracted with
[upstream CUE4Parse](https://github.com/FabianFG/CUE4Parse).

**[Browse and download the data →](https://github.com/raidertool/asset-index/tree/data)**

`main` contains the extractor, mappings, tests and workflows. Generated files live
on [`data`](https://github.com/raidertool/asset-index/tree/data).

**Preview:** the files below describe the new export. The data branch retains
the [legacy format](https://github.com/raidertool/asset-index/tree/6a68cb6608e9d2eaf802ad239172038c6596b21f)
until its validated replacement is published.

## Use the data

| File | Contents |
| --- | --- |
| `asset_index.csv` | `asset_id,asset_name,display_name,description,image,wide_image` |
| `asset_localizations.csv` | `asset_id,locale,display_name,description` |
| `assets.json` | Definition and metadata references, presentation sources, localized text and image roles |
| `resources.json`, `images/` | All exported images, including UI textures without an asset ID |
| `localization/` | Compressed translation dictionaries |
| `metadata.json`, `coverage.json` | Snapshot identity and extraction diagnostics |

Use the `data` branch in raw download URLs, or pin a snapshot tag/commit for
repeatable downloads. Old `/main/…` data URLs must change to `/data/…`.

IDs are decimal strings in JSON; read CSV IDs as strings too. One ID can have
multiple definitions and image roles. The core CSV is a convenient projection;
`assets.json` retains the detail.

`assets.json.text` and `asset_localizations.csv` fields stay blank when the selected
translation is absent.
Authored source text is separate in `presentation.name.source`,
`presentation.description.source` and candidate references. The English core CSV
uses the selected English translation, with authored source text as its explicit
fallback. Culture-invariant text applies to every locale.

Image paths preserve the original texture name and package directories. For
example, `/Game/Pioneer/UI/Icon.Icon` becomes `images/Game/Pioneer/UI/Icon.png`.
Use the published reference; several assets can share one image. `wide_image`
contains a supported whole-asset image that is wider than it is tall, when one
exists. A map region or control icon does not become an item's wide image.

The new CSV renames `display_name_en` and `description_en` to `display_name` and
`description`. Technical names can change, and previously populated names or
descriptions can become blank. Use the published image paths instead of
constructing legacy filenames.

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

Pull-request CI runs offline without Steam credentials. The separate
[update workflow](.github/workflows/extract.yml) uses released source on `main`
for live extraction. Manual runs default to exporting public files without
publishing. Complete previews and authentication logs are never uploaded.

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
`data`. It never updates `main`:

```sh
dotnet run --project src/PublishSnapshot -c Release -- \
  <preview-directory> <remote> <extractor-commit> <Steam-manifest-id>
```

The data digest covers the sorted generated Git blob inventory; it excludes
metadata, coverage and source. The same manifest and content create no new
snapshot even after source-only commits. A new Steam manifest creates a snapshot
identity even when its content is unchanged. The data branch and its tag are pushed
atomically without overwriting a concurrent writer.

`--export <preview> <new-directory> <extractor-commit> <Steam-manifest-id>`
performs the same validation and writes only public files locally. Full discovery
evidence and diagnostic messages are excluded.

## Automatic updates

Once enabled, GitHub checks public Steam version information every five minutes.
Only an unpublished release triggers Steam login and extraction. Failed attempts
have a one-hour cooldown; later checks retry. GitHub can delay or skip scheduled
runs, and disables schedules after 60 days without repository activity. Manual
runs remain available; `force` re-extracts the current release. Dispatch manual
runs after an active update finishes: newer scheduled runs can replace a pending run.

Extraction uses the latest immutable `exfil-v*` release on `main`. A separate
job publishes its validated public export; source changes alone do not trigger
extraction. Public export artifacts expire after one day. No game files, Steam
caches, full discovery records or authentication logs are uploaded or cached.

Maintainers: restrict the `steam-extraction` and `asset-publication` environments
to `main`. Put only `STEAM_USERNAME` and `STEAM_PASSWORD` in `steam-extraction`;
the publisher uses the repository's `GITHUB_TOKEN`. Confirm branch/tag rules allow
its atomic `data`/tag update while protecting `main` through PRs. The `data` branch
must exist before publication. Test a manual export, then explicitly select `publish` for the
first publication. Set `ASSET_UPDATES_ENABLED=true` to enable scheduled updates.
Until then, the schedule skips extraction; manual runs remain available.

See [CONTRIBUTING.md](CONTRIBUTING.md) for tests and mapping updates, and
[implementation notes](docs/notes.md) for selection and validation rules.
