# ARC Raiders asset data

Game asset IDs, names, descriptions, translations and images extracted by
[Exfil](https://github.com/raidertool/asset-index/tree/main).

## Use the data

| File | Contents |
| --- | --- |
| [asset_index.csv](asset_index.csv) | `asset_id,asset_name,display_name,description,image,wide_image` |
| [asset_localizations.csv](asset_localizations.csv) | `asset_id,locale,display_name,description` |
| [assets.json](assets.json) | Definition and metadata references, presentation sources, localized text and image roles |
| [resources.json](resources.json), [images/](images/) | All exported images, including UI textures without an asset ID |
| [localization/](localization/) | Compressed translation dictionaries |
| [metadata.json](metadata.json), [coverage.json](coverage.json) | Snapshot identity and extraction diagnostics |

Use the `data` branch in raw download URLs, or pin a snapshot tag/commit for
repeatable downloads. Old `/main/…` data URLs must change to `/data/…`.

IDs are decimal strings in JSON; read CSV IDs as strings too. One ID can have
multiple definitions and image roles. The core CSV is a convenient projection;
`assets.json` retains the detail.

Each asset’s `text` entries in `assets.json`, and the localization CSV fields,
stay blank when the selected translation is absent.
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

## Snapshot versions

Pin a snapshot tag or commit for repeatable downloads. Tags use
`arc-<SteamManifestId>-<contentSha25612>`; `metadata.json` records the source
commit, Steam identifiers and full data hash. Older snapshots retain their
original formats and tag names.

`coverage.json` summarizes extraction counts and known limitations. It is not
test coverage or a percentage of the whole game; most consumers can ignore it.

Contribute extractor changes on `main`; dataset documentation changes target
`data`. See [CONTRIBUTING.md](https://github.com/raidertool/asset-index/blob/main/CONTRIBUTING.md).

## Credits

Thanks to xivy, whose skills made this possible.
