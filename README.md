# ARC Raiders Asset Index

Extract ARC Raiders game asset IDs, names, descriptions, and images using
[upstream CUE4Parse](https://github.com/FabianFG/CUE4Parse).

**Preview:** the new extractor and its output format are under review. Coverage
is incomplete, and compatibility is not guaranteed yet. Runs produce local
artifacts; they do not publish data or update a database.

The existing root [CSV](asset_index.csv), [localizations](asset_localizations.csv),
[images](images/), and [schema](schema.json) belong to the previous published
dataset. They are separate from the new preview output.

## Run locally

Install the .NET SDK version in [global.json](global.json), currently 10.0.302,
then initialize the pinned dependency:

```sh
git submodule update --init --recursive
```

Point the extractor at your installed game's `PioneerGame/Content/Paks` directory.
Use a new or empty output directory:

```sh
CUE4PARSE_SKIP_NATIVE=true dotnet run --project src/AssetIndex -c Release -- \
  --game-dir "/path/to/ARC Raiders/PioneerGame/Content/Paks" \
  --output .work/preview
```

The bundled [usmap](mappings/ArcRaiders.usmap) describes game property layouts.
It must match your game files. Use `--usmap /path/to/new.usmap` to try another
mapping. Removing this dependency is future work.

The default game-content decryption key is
[public](https://github.com/ARC-Data-Raiders/DataRaiders/blob/main/aes.txt).
Set `ARC_AES_KEY` locally to override it for a newer build.

To read Steam depot files without a full installation, mount them with
[SteamDepotFS](https://github.com/raidertool/SteamDepotFS#usage) using your own
local Steam credentials, then pass the mounted `PioneerGame/Content/Paks`
directory as `--game-dir`. Follow SteamDepotFS's platform and authentication
instructions; this extractor does not manage Steam accounts.

## Preview output

| Path | Contents |
| --- | --- |
| `assets.json` | Discovered IDs, asset paths/classes, localized text, and image references/statuses |
| `images/` | Decoded images |
| `coverage.json` | Extraction counts and unresolved or failed extraction details |

IDs are decimal strings so JavaScript can preserve every signed 64-bit value.
Each ID retains all associated definitions and UI metadata; it can have multiple
image references. Unsupported image sources are reported in coverage.

Review coverage before using a preview. A successful run does not prove every
game asset was found. Missing data may need a mapping update or an extractor fix.
Local output under `.work/` is ignored by Git.

## Layout

```text
src/AssetIndex/          Extractor; focused files by responsibility
tests/AssetIndex.Tests/  Offline tests
mappings/               Current game property mapping
vendor/CUE4Parse/        Pinned upstream Git submodule
```

## Contribute

Run the offline tests:

```sh
CUE4PARSE_SKIP_NATIVE=true dotnet test tests/AssetIndex.Tests/AssetIndex.Tests.csproj -c Release
```

PR CI runs these tests without Steam credentials or game files. See
[CONTRIBUTING.md](CONTRIBUTING.md) for coverage fixes and mapping updates.
