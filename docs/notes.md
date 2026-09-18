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

Every effective mounted package gets an export-header inventory. Typed candidates,
registry UI textures and relevant references require decoded bodies; known
unrelated exports can remain header-only. Exact object/outer paths and superclass
references must agree. A loaded package does not mean every body or native payload
was decoded.

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
docs are preserved on `main`. Atomic main/tag updates reject concurrent writers;
existing tags are checked and never replaced. Matching manifest/content is a
no-op even when the current main commit includes newer source changes. The tag's
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
