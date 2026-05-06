# ARC Raiders Game Asset Index

**Arc Raiders Steam Build: 23029453 (2026-05-05)**

Standalone CSV index of ARC Raiders game asset IDs and internal asset names.

## Automation

This repository is regenerated when Steam publishes a new ARC Raiders build, or the (private) extraction codebase ("exfil") changes. *This means `HEAD` should always have content from the latest patch*.

Generated CSV and image files are committed only when their contents change. The snapshot tag is updated on every game update, even without asset changes. Code changes (without a game update) are only tagged when the assets change (e.g. the code is updated to extract additional metadata).

New snapshot tags use `arc-<steam-build-id>-exfil-<exfil-version>`. The current snapshot tag is `arc-23029453-exfil-v0.1.8`.

## Dataset

The canonical dataset is [asset_index.csv](./asset_index.csv). It is generated entirely from the game's files.

Columns:

```csv
asset_id,asset_name,display_name_en,description_en
```

- `asset_id` - integer game asset ID.
- `asset_name` - internal game asset string.
- `display_name_en` - English display name, when found in the game files.
- `description_en` - English description text, when found in the game files.

Current snapshot:

| Metric | Count |
| --- | ---: |
| Unique asset IDs | 3,997 |
| Internal asset names | 3,997 |
| English display names | 2,475 |
| English descriptions | 729 |
| PNG icons | 1,132 |
| Wide icon variants | 103 |

This is intended to cover the integer game asset IDs currently extractable from the game files. It is not a complete index of every Unreal asset, and some display names and descriptions are still blank. More work is being done to extract additional strings.

Every `asset_name` value is an actual string found in the game files. Some currently use `DA_Persistence_*` names because a more specific item or structure name could not be fully resolved yet; those names may become more specific in future revisions.

The `images/` directory contains exported PNG icons named from `asset_name` when an icon could be resolved from the game files. Weapons may also include a wide equipped icon named `<asset_name>_wide.png`.

## Using The Index

The ARC Raiders API usually uses integer asset IDs, not readable names. Join those IDs against `asset_index.csv` to recover names like `DA_Item_*`, `DA_OI_*`, `DA_Inventory_*`, and `DA_ModSlot_*`.

The index includes both item leaves and structural inventory assets. The structural `DA_Inventory_*` assets describe container trees, slots, and loadout frames. The leaves are usually actual items, cosmetics, mods, or unlocks.

A simplified inventory tree looks like this:

```text
DA_Inventory_TreeRoot
├── DA_Inventory_ContainerSlot_Stash
│   └── DA_Inventory_StashContainer_T01 ... DA_Inventory_StashContainer_T10
│       └── DA_Inventory_ContentSlot_AnyItem
│           └── DA_Item_*
├── DA_Inventory_LoadoutFrameSlot
│   └── DA_Inventory_LF_Standard / DA_Inventory_LF_*
│       ├── DA_Inventory_LF_*_ContainerSlot_Backpack
│       │   └── DA_Inventory_LF_*_ContainerItem_Backpack
│       │       └── DA_Inventory_ContentSlot_AnyItem
│       │           └── DA_Item_*
│       ├── DA_Inventory_LF_*_ContainerSlot_Belt
│       │   └── DA_Inventory_LF_*_ContainerItem_Belt
│       │       └── DA_Inventory_ContentSlot_Belt_Generic
│       │           └── DA_Item_*
│       ├── DA_Inventory_LF_*_ContainerSlot_Firearm1
│       │   └── DA_Inventory_LF_*_ContainerItem_Firearm1
│       │       └── DA_Inventory_ContentSlot_Firearm_Generic
│       │           └── DA_Item_*
│       ├── DA_Inventory_LF_*_ContainerSlot_Firearm2
│       │   └── DA_Inventory_LF_*_ContainerItem_Firearm2
│       │       └── DA_Inventory_ContentSlot_Firearm_Generic
│       │           └── DA_Item_*
│       ├── DA_Inventory_LF_*_ContainerSlot_SafePocket
│       │   └── DA_Inventory_LF_*_ContainerItem_SafePocket
│       │       └── DA_Inventory_ContentSlot_SafePocket
│       │           └── DA_Item_*
│       └── DA_Inventory_LF_*_ContainerSlot_Armor
│           └── DA_Inventory_LF_*_ContainerItem_Armor
│               └── DA_Inventory_ContentSlot_Armor_*
│                   └── DA_Item_*
├── DA_Inventory_ContainerSlot_Augment
│   └── DA_Inventory_AugmentContainer
│       └── DA_Inventory_ContentSlot_Augment_Generic
│           └── DA_Item_Augment_*
└── DA_Inventory_ContainerSlot_ExpeditionStash
    └── DA_Inventory_ExpeditionStashContainer_T01 ... T05
        └── DA_Inventory_ContentSlot_AnyItem
            └── DA_Item_*
```

Weapons and other moddable items can also reference mod-slot assets:

```text
DA_Item_SMG_LowTier_01_Quality0
├── DA_ModSlot_Firearm_Muzzle
│   └── DA_Item_Mod_*
├── DA_ModSlot_Firearm_UnderBarrel
│   └── DA_Item_Mod_*
├── DA_ModSlot_Firearm_MagazineLight
│   └── DA_Item_Mod_*
└── DA_ModSlot_Firearm_Stock
    └── DA_Item_Mod_*
```

Common prefixes:

- `DA_Item_*` - game item assets.
- `DA_OI_*` - online item or unlock-style assets, often cosmetics.
- `DA_Inventory_*` - inventory roots, containers, slots, and loadout-frame structure.
- `DA_ModSlot_*` - attachment/mod slot definitions.
- `DA_Persistence_*` - persistence or progression metadata assets.

Common shorthand:

- `LF` - loadout frame.
- `OI` - online item.
- `T1`, `T2`, `T3`, etc. - tier markers.
- `T01` through `T10` - stash or expedition stash container levels.
- `XP` - experience points.
- `XL` - extra large, such as an XL safe pocket.
- `DBNO` - down but not out.


## Schema

[schema.json](./schema.json) describes the CSV format and column contract.

## Contributing

PRs that improve coverage, fix asset names, or add missing display strings are welcome. Please keep changes sourced from game files where possible.