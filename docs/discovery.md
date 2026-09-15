# Discovery

The catalog is a projection of typed client data. An asset ID does not identify
every string, texture, UI object or API node.

## Read the source

`Discovery/Registry.cs` inventories every registry entry. The crawler decodes
data assets, UI metadata, tables, blueprints, unknown classes, unindexed packages,
and UI textures selected by their registry group. Typed references also bring in
texture/material dependencies. Other registered classes remain inventoried.

IoStore package bytes use a 256 MiB memory cache and a temporary disk spool,
deleted when the provider closes. Lazy readers can revisit evicted bytes;
decoded objects and image pixels use separate memory.

`EvidenceReader` visits CUE4Parse property tags, arrays, sets, map keys/values,
structs and text histories directly. Small native adapters cover data/curve/string
tables, class references, field declarations, delegates, and cached material fields.
It preserves property pointers, reference kinds,
nulls, empty overrides and exact numeric strings. It never interprets an ordinary
string as an object reference or uses CUE4Parse's JSON export as a parsing layer.

Property headers retain each tag's exact name/type, nullable static-array index/size
and serialization mode. Values use ordinal paths such as `/Properties/0`; nested
tagged structs add their own `/Properties/0`. Repeated scalar names remain separate
records; catalog lookup still rejects ambiguity. Ordinals describe decoded order,
not declaration or schema slots.

The pinned decoder cannot safely expand static arrays in unversioned runtime
class layouts. Those layouts and runtime ancestors produce diagnostics; tagged
packages and native usmap arrays use different decoders. Repeated indexed elements also remain unresolved. Nested struct
layout identity is not always retained upstream, so this check does not certify
every nested layout or recover fields skipped by the decoder.

`discovery/objects.jsonl.gz` retains unassociated fields and strings;
`registry.jsonl.gz` and `packages.jsonl.gz` show selection and decode coverage.
`files.jsonl.gz` inventories effective mounted UE package paths and their resolved
`registryPackages`. Empty lists identify unindexed inputs; each must have an
attempt in `packages.jsonl.gz`. The crosswalk uses the provider's mount resolution,
including package IDs, and excludes older shadowed archive versions and payloads.
`localization/` retains the game's merged translation dictionaries. `resources.json`
indexes UI/referenced textures and supported material images independently of catalog ownership.

```sh
gzip -dc .work/preview/discovery/objects.jsonl.gz | rg 'ResearchPoints'
```

Binary native payloads and composite-curve evaluation are outside field discovery.
Unsupported compound fields and failed reads are explicit diagnostics. A loaded
package does not mean every export or every native payload was decoded. Selected
object references must match a decoded export and its complete outer chain.

## Assign presentation

- `Assets.cs` follows explicit identity links and enabled ID overrides. Local
  definitions with no identity stay in discovery.
- `TextRoles.cs` assigns meaning to fields on specific UI/definition classes.
  `Text.cs` prefers UI presentation, retains alternatives, and leaves conflicting
  peers unresolved. `presentation` records chosen keys and defining objects.
- `Presentation.cs` joins loadout container types and slot references to the
  corresponding UI container label; its full join path remains in each row.
- `Images.cs` associates declared image fields. `MaterialIcons.cs` renders the
  verified color-scheme and Close Scrutiny graphs under
  [defined conditions](materials.md). Other material inputs remain evidence;
  an ingredient alone is not a rendered icon.

Historical output only finds changes. Do not restore a name or image without a
current identity/presentation relationship. Add a small fixture for that exact
relationship and check neighboring cases, including changed names and nulls.
