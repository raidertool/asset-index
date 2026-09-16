# Discovery

The catalog is a projection of typed client data. An asset ID does not identify
every string, texture, UI object or API node.

## Read the source

`Discovery/Registry.cs` inventories every registry entry. Every effective mounted
package has its export headers inspected. Actual class ancestry selects data assets,
UI metadata, tables, blueprints and unknown classes, including unregistered exports.
Registry UI textures and exact typed texture/material or runtime `Struct` targets
(including classes and user-defined structs) are also decoded;
known Actor, world and other noncandidate bodies remain inventoried. Metadata
ambiguity is a diagnostic, never a silent exclusion.

IoStore reads fetch decoded package pages on demand, with a 256 MiB memory cache.
Fetched pages remain in a temporary disk spool until the provider closes, so lazy
readers can revisit evicted bytes. Upstream handles compressed-block reads;
decoded objects and image pixels use separate memory. Package bodies are weakly
cached: the crawler writes evidence and retains detached catalog facts, so unused
object graphs can be collected. Deferred image rendering reloads the exact physical
file and export index. An active package can still retain its imported dependencies.
Bulk texture payloads use the pinned upstream provider's resolution rules.

`EvidenceReader` visits CUE4Parse property tags, arrays, sets, map keys/values,
structs and text histories directly. Small native adapters cover data/curve/string
tables, class references, field declarations, delegates, and cached material fields.
It preserves property pointers, reference kinds,
nulls, empty overrides and exact numeric strings. It never interprets an ordinary
string as an object reference or uses CUE4Parse's JSON export as a parsing layer.

Homogeneous fixed-width numeric arrays use one `numeric-le-base64` value: canonical
property type plus `[]`, little-endian scalar bytes, and count inferred from byte
length. Signed integers and IEEE float bits (including NaNs and negative zero) are
lossless. Plain bytes retain `binary-base64`; enums, booleans and mixed arrays keep
element evidence. This limits evidence expansion while preserving decoded values.

Material references come from serialized properties and cached bindings. Unrelated
package imports do not become material edges; registry and mounted-file inventories
retain the broader content scope.

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
`registry.jsonl.gz` and `exports.jsonl.gz` retain registry entries and export headers.
Headers record `classPath` and the export's own nullable superclass `superPath`;
these links come from metadata without loading bodies.
`packages.jsonl.gz` records each physical path, actual package `name`, total export
count, and sorted `selected`/`decoded` export-index sets. Package `succeeded` and
report `loaded` mean valid inspected headers and successful selected bodies;
they do not mean every body was decoded. Selected registry entries require an exact
export path or an explicit, validated `GeneratedClass` tag.
`files.jsonl.gz` inventories effective mounted UE package paths and their resolved
`registryPackages`. Every effective mounted input must have a header-read attempt
in `packages.jsonl.gz`; empty lists identify unindexed inputs. The crosswalk uses the provider's mount resolution,
including package IDs, and excludes older shadowed archive versions and payloads.
Registry and file inventories are saved before crawling. Every 30 seconds, stderr
reports the active discovery operation, requested package and export index. Nested
decoder dependencies may be loaded within that operation. Interrupted object streams
remain temporary; inventories alone do not establish completed extraction.
`localization/` retains the game's merged translation dictionaries. `resources.json`
indexes registry UI textures and explicit catalog image roles, including supported
material images. Other referenced textures remain discovery evidence; they do not
automatically become standalone PNGs.

```sh
gzip -dc .work/preview/discovery/objects.jsonl.gz | rg 'ResearchPoints'
```

Binary native payloads and composite-curve evaluation are outside field discovery.
Unsupported compound fields and failed reads are explicit diagnostics. A loaded
package does not mean every export or every native payload was decoded. Selected
object references must match their complete outer chain in the inspected headers.
Referenced widgets, widget trees, panel slots, class defaults and templates need
decoded bodies. Known unrelated targets can remain header-only.

## Assign presentation

Runtime classes follow their actual superclass references into native usmap
ancestry. Only that native ancestry establishes catalog roles; runtime short names
cannot impersonate it. Projection verifies header/body parents and scalar typed
field declarations. Missing or conflicting schemas produce diagnostics.

- `Assets.cs` follows explicit identity links and enabled ID overrides. Local
  definitions with no identity stay in discovery.
- `TextRoles.cs` assigns meaning to fields on specific UI/definition classes.
  `Text.cs` prefers UI presentation, retains alternatives, and leaves conflicting
  peers unresolved. `presentation` records chosen keys and defining objects;
  `presentation.candidates` retains the other authored text and its source fields.
  Valid conflicts keep a null primary and a coverage notice; failed reads remain errors.
  Unlock instructions remain `unlock-description` candidates, separate from an
  item's description. NPC-specific UI names take precedence; session modifiers
  use their typed description when no name field exists.
- `Presentation.cs` joins loadout container types and slot references to the
  corresponding UI container label; its full join path remains in each row.
- `InventoryRootPolicy.cs` maps the five persistent root fields to Stash/Augment
  categories. Typed root, default-container and eligibility links select their
  localized UI labels. `inventoryRoots` retains this context; direct names take precedence.
- `VisualSlotLabels.cs` joins a slot's tag query through matching skin identities
  and UI type tags to its navigation label. Ambiguous joins stay unresolved.
- `Images.cs` associates declared image fields, including class-specific clan and
  environmental images. `MapImages.cs` retains `MapAreas[n].HeaderImage` and
  `MapWidgetSettings.MapTexture`; local containers replace the whole template field.
  `MaterialIcons.cs` renders the
  verified color-scheme and Close Scrutiny graphs under
  [defined conditions](materials.md). Other material inputs remain evidence;
  an ingredient alone is not a rendered icon.

Historical output only finds changes. Do not restore a name or image without a
current identity/presentation relationship. Add a small fixture for that exact
relationship and check neighboring cases, including changed names and nulls.
