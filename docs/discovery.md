# Discovery

The catalog is a projection of typed client data. An asset ID does not identify
every string, texture, UI object or API node.

## Read the source

`Discovery/Registry.cs` inventories every registry entry. The crawler decodes
data assets, UI metadata, tables, blueprints, unknown classes, unindexed packages,
and UI textures selected by their registry group. Typed references also bring in
texture/material dependencies. Other binary media remain inventoried.

`EvidenceReader` visits CUE4Parse property tags, arrays, sets, map keys/values,
structs and text histories directly. Small native adapters cover data/curve/string
tables and class references. It preserves property pointers, reference kinds,
nulls, empty overrides and exact numeric strings. It never interprets an ordinary
string as an object reference or uses CUE4Parse's JSON export as a parsing layer.

`discovery/objects.jsonl.gz` retains unassociated fields and strings;
`registry.jsonl.gz` and `packages.jsonl.gz` show selection and decode coverage.
`localization/` retains the game's merged translation dictionaries. `resources.json`
indexes decoded UI/referenced textures independently of catalog ownership.

```sh
gzip -dc .work/preview/discovery/objects.jsonl.gz | rg 'ResearchPoints'
```

Binary native payloads and composite-curve evaluation are outside field discovery.
Unsupported compound fields and failed reads are explicit diagnostics. A loaded
package does not mean every export or every native payload was decoded.

## Assign presentation

- `Assets.cs` follows explicit identity links and enabled ID overrides. Local
  definitions with no identity stay in discovery.
- `TextRoles.cs` assigns meaning to fields on specific UI/definition classes.
  `Text.cs` prefers UI presentation, retains alternatives, and leaves conflicting
  peers unresolved. `presentation` records chosen keys and defining objects.
- `Presentation.cs` joins loadout container types and slot references to the
  corresponding UI container label; its full join path remains in each row.
- `Images.cs` associates declared image fields. Material inputs are evidence;
  an ingredient texture alone does not establish the rendered icon.

Historical output only finds changes. Do not restore a name or image without a
current identity/presentation relationship. Add a small fixture for that exact
relationship and check neighboring cases, including changed names and nulls.
