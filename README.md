# ARC Raiders Asset Index

Exfil extracts game asset IDs, localized text and images using
[upstream CUE4Parse](https://github.com/FabianFG/CUE4Parse).

**[Browse and use the dataset →](https://github.com/raidertool/asset-index/tree/data)**

Source, mappings and workflows live on `main`; generated files live on `data`.

## Run locally

Install the .NET SDK version specified in [global.json](global.json), then:

```sh
git submodule update --init --recursive
CUE4PARSE_SKIP_NATIVE=true dotnet run --project src/AssetIndex -c Release -- \
  --game-dir "/path/to/ARC Raiders/PioneerGame/Content/Paks" \
  --output .work/preview
```

Use a new or empty output directory and game files compatible with the bundled
[mapping](mappings/README.md). [SteamDepotFS](https://github.com/raidertool/SteamDepotFS#usage)
can provide the game files on demand using your own Steam credentials.

See [CONTRIBUTING.md](CONTRIBUTING.md) for testing, mappings and running updates,
and [implementation notes](docs/notes.md) for extraction rules and limitations.
