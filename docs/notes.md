# Implementation notes

## Identity and presentation

The catalog projects typed client data. IDs do not identify every string, texture
or UI object. Identity comes from declared fields, enabled overrides and explicit
persistence links. Unidentified definitions remain discovery evidence.

Runtime classes follow actual superclass references into the native mapping.
Short class names cannot establish a catalog role. Projection checks declaration
types, inheritance and reference identity; ambiguity produces diagnostics.

`presentation` records the chosen text reference and every supported candidate.
UI-specific fields take precedence over generic definitions. Conflicting peers
or an explicit empty override leave the primary unresolved. Missing translations
stay blank; source text remains available separately. Unlock instructions retain
their own role. NPC-specific names win over generic item names; session modifiers
use their typed description when no name exists.

Loadout containers and visual slots use typed UI relationships. The five persistent
root fields map to Stash/Augment categories, then follow default-container and
eligibility links to localized labels. Direct names take precedence. Visual-slot
tag queries must have an unambiguous complete match. These relationships remain
in presentation provenance.

Image associations come from declared fields. Map-area headers and map-widget
textures retain their own roles. Local containers replace the whole template field.
Every exported resource is retained even without a catalog owner. Original
texture names and package paths determine PNG paths; an ingredient texture is not
substituted for an unsupported finished material icon.

## Evidence and validation

Archive read priority determines package selection. Equal-priority copies must
agree on content hashes, owned payloads and package-store context before sharing
a stable representative. Unknown or conflicting copies stop extraction. Path and
package-ID lookup use the same winner; older-priority versions remain available
for upstream import resolution.

Every effective mounted package gets an export-header inventory. Typed candidates,
registry UI textures and relevant references require decoded bodies; known
unrelated exports can remain header-only. Exact object/outer paths and superclass
references must agree. A loaded package does not mean every body or native payload
was decoded.

Missing layouts can remain header-only when independently verified class-family
evidence excludes catalog data; this never supplies a guessed decoding schema.
Ordinary exploratory references may be unavailable only when the inspected package
indexes prove absence. Catalog dependencies remain strict. Coverage format 3
reports these limits in `exploration`: unavailable soft/hard reference counts and
unmapped non-catalog export counts. Private evidence stays in `discovery/`.

Evidence preserves property pointers, reference kinds, nulls, explicit empty
values and exact numbers. Property ordinals describe decoded order, not declaration
slots. Homogeneous fixed-width numeric arrays use `numeric-le-base64`: canonical
array type, little-endian bytes and an inferred element count. Ordinary bytes use
`binary-base64`. Encodings and lengths must be canonical and valid.

The publisher checks the complete preview: source identities, selected and omitted
text candidates, template owners, dictionary values, image-role relationships,
resource paths, PNG decoding, mapping declarations and evidence counts. An absent
translation cannot silently become source text in localized output. Conflicts and
unsupported material images remain explicit. Success is not proof of complete
game coverage.

Validation uses a private disk copy so later input changes cannot alter the
validated payload. Full discovery streams stay out of published snapshots.
Generated output includes both CSVs, catalog, resources, all exported PNGs,
localization, coverage and metadata. Each published file must fit GitHub's 100 MiB
blob limit.

Public CI validates the full preview before transferring its public export to a
fresh publisher job. `--export-digest` hashes every exported file, including
coverage and metadata; this handoff hash is separate from data identity below.
`--publish-export` requires that hash, the source/Steam identities and the previously
observed metadata Git blob. It rechecks public structure and exact bytes; original
object evidence was checked upstream. Never accept this digest from an untrusted
run or execute files from the transferred artifact.

## Data identity

`metadata.json` uses this shape:

```json
{
  "formatVersion": 2,
  "contentSha256": "<64 lowercase hexadecimal characters>",
  "extractorCommit": "<40 lowercase hexadecimal characters>",
  "steam": {
    "appId": 1808500,
    "depotId": 1808501,
    "manifestId": "<positive decimal u64>"
  }
}
```

Data tags are `arc-<manifestId>-<contentSha25612>`. The full hash covers the sorted
published-content Git blob inventory, excluding coverage and metadata. Source and
docs live on `main`. Atomic data/tag updates reject concurrent data writers;
existing tags are checked and never replaced. Matching manifest/content is a
no-op; changes on `main` do not move the data branch. The tag's
snapshot metadata retains the extractor commit that produced that content.

Software tags occupy the separate `exfil-vX.Y.Z` namespace. The fixed legacy
baseline is `arc-24017548-exfil-v0.13.2`; its data history need not be an ancestor
of the new source history. Later software tags must follow main ancestry. The
release workflow only handles successful push CI from this repository and checks
the target against remote main before pushing a tag. It never writes main.

## Supported material icons

Two material families have explicit renderers: character color-scheme instances
and the Close Scrutiny icon. Parent-package hashes pin supported inputs and
defaults. Unknown overrides, changed inputs and invalid arithmetic produce
diagnostics.

Output is a 256 × 256 sRGB PNG with straight alpha, pixel-center UVs, mip 0 and
bilinear filtering. Color-scheme backgrounds wrap and bevels clamp; Close Scrutiny
preserves the texture address modes and fixes its animation input to zero. White
vertex tint and no widget effects are assumed. These conditions do not reproduce
arbitrary engine rendering. Tests use synthetic images and independent arithmetic
fixtures. Unsupported materials retain evidence rather than a guessed icon.

## Disk usage

SteamDepotFS reads requested data with an 8 GiB chunk-cache limit; extraction
spools selected package pages rather than downloading the whole game. The package
spool and Steam chunk cache are removed before export; the generated preview
remains for validation.

The [2026-09-18 public run](https://github.com/raidertool/asset-index/actions/runs/35401161382)
of `exfil-v1.0.1` on `ubuntu-latest` measured:

| Phase | Peak additional disk use |
| --- | ---: |
| Extraction | 13.55 GiB |
| Export, after extraction cleanup | 2.35 GiB |

These are five-second samples relative to each phase's starting disk use. The
phases run sequentially, so their peaks should not be added. They are observations
for one run, not guaranteed limits. CI stops the process if sampled free space
falls below the 2 GiB safety reserve; that reserve is not the total storage
requirement.

## Local publication

Validate a complete preview and write only public files locally:

```sh
dotnet run --project src/PublishSnapshot -c Release -- \
  --export <preview-directory> <new-export-directory> <extractor-commit> <Steam-manifest-id>
```

The direct publisher validates the same preview, then updates `data` and its tag:

```sh
dotnet run --project src/PublishSnapshot -c Release -- \
  <preview-directory> <remote> <extractor-commit> <Steam-manifest-id>
```

Public `coverage.json` contains verified output counts and aggregate unresolved
reference/schema counts. `images` counts IDs with an exported image; discovery
`resources` counts exported image resources. English name/description counts use
resolved English translations and can differ from CSV source-text fallbacks.
A successful status means validation passed, not that every game object has a
name or that all game content was decoded. Full preview diagnostics stay local.
