# Arc Raiders Game Asset Index

Game asset ID to string mapping for Arc Raiders.

## Description

This repo contains a standalone index of ARC Raiders game asset IDs and related
names in CSV form.

The game's internal APIs work with asset IDs that are difficult to understand.
This index gives internal game strings and display names where known. A `note`
field also provides observations or hints at what an asset might be.

This is larger than the dump achievable from the debug menu. However, it does not
include quests and other types of assets available in the `/game/data` endpoint,
and currently focuses on what the `/inventory` endpoint might return (including
structural nodes, cosmetics, and emotes).

The canonical dataset lives in [asset_index.csv](./asset_index.csv), and
[schema.json](./schema.json) describes the CSV schema. Additional information
about each asset can be found from the `/game/data` endpoint.

## Structure

The `/game/data` endpoint describes the type system. The `/inventory` endpoint
describes an instantiation of that type system.

The inventory data uses structural `DA_Inventory_*` nodes that eventually lead
to item leaves like `DA_Item_*`. Each node has a `slots` array that contains
instance IDs (UUIDs) of child items. Some nodes (such as XP) have no parent,
leading to multiple trees (a forest).

The `/inventory` endpoint returns these as a flat list rather than nested
JSON structure, so you will have to create the conceptual tree yourself by
searching for key asset IDs and following their child instance IDs.

Here is a depiction of some of the most important inventory structures:

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
│       ├── DA_Inventory_LF_*_ContainerSlot_Weapon1 / _Firearm1
│       │   └── DA_Inventory_LF_*_ContainerItem_Weapon1 / _Firearm1
│       │       └── DA_Inventory_ContentSlot_Firearm_Generic
│       │           └── DA_Item_*
│       ├── DA_Inventory_LF_*_ContainerSlot_Weapon2 / _Firearm2
│       │   └── DA_Inventory_LF_*_ContainerItem_Weapon2 / _Firearm2
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

And here is an example tree that a weapon forms:

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

Some additional notes on naming and structure:

- `LF` means loadout frame.
- `OI` means online item. In practice these are unlock-like or
  cosmetic assets such as outfits, backpack charms, and backpack attachments.
- `T1`, `T2`, `T3`, and so on are tier markers. `T01` through `T10` are also
  used for stash and expedition stash container levels.
- `XP` means experience points.
- `XL` means extra large, as in `DA_Inventory_ContentSlot_XLSafePocket` (the safepocket
  that can hold guns).
- `DBNO` is "down but not out".
- `DA_Inventory_TreeRoot` is the top-level inventory root.
- `DA_Inventory_LoadoutFrameSlot` points at the currently equipped loadout
  frame.
- The specific `DA_Inventory_LF_*` root in use depends on the augment equipped
  in the augment slot. For example, a `DA_Item_Augment_*` usually maps to the
  matching `DA_Inventory_LF_*` loadout root.
- `DA_Inventory_LF_Standard` is the default "naked" loadout frame used when no
  augment-specific loadout is selected.
- `DA_Inventory_LF_*` roots represent full loadout frame trees such as combat,
  looting, utility, and other archetype or tier variants.
- A `ContainerSlot_*` node is the structural branch entry point that
  links to the next container or subtree.
- A `ContainerItem_*` node is the concrete holder attached to that
  branch, such as a backpack container or weapon holder.
- A `ContentSlot_*` node is the terminal slot that directly holds a
  `DA_Item_*`.


## Methodology

- I started with a complete memory dump from a friend (I will update this doc with
  credits soon).
- It had all IDs, but was missing some internal strings, so I cross referenced it with an incomplete
  memory dump.
- I inferred many of the display names myself, but some were AI generated, and there are many missing.
- For newer items I have inferred only the display name and not the internal name.
- In some cases I only have an AI-generated note, inferred from surrounding context returned by the
  `/game/data` API.


## Contributing

I believe this to be a fairly complete list, but it still needs some love and care. Please submit PRs
if with additional asset IDs, internal strings, or improved display names. Furthermore, I'd love to
add a stable `slug` field for things like human-readable image lookup.
