# Snapshot publication

Activation is pending. Normal publication requires an existing `data` branch with
the published snapshot below, `metadata.json`, and its matching
lightweight tag. Initialization is a separate explicit mode.
Game-backed workflow jobs require a private repository. Public CI runs offline;
the safe handoff from private validation to public publication is not implemented.

- `assets.json`: catalog rows and presentation provenance.
- `resources.json` and `images/`: every image referenced by the catalog.
- `localization/<locale>.jsonl.gz`: merged localization dictionaries, including English.
- `coverage.json`: extraction counts, errors, notices, mapping hash, and discovery scope.

The complete preview is validated first. Discovery streams and extra UI images
remain private; they are excluded from Git. Coverage retains
the full extraction counts. Every published file must be at most 100 MiB.

```sh
dotnet run --project src/PublishSnapshot -c Release -- \
  <preview-directory> <remote> <extractor-commit> <manifest-id>
```

The preview must report success without errors, have a definition for every ID,
and pass provenance, evidence-count, gzip integrity, resource-reference, and full
PNG decoding checks. Every catalog source and image resource must have object evidence;
every PNG must belong to the resource inventory. Image filenames hash their
declared resource paths, not their PNG bytes.
Text selection must match candidate source/role precedence; conflicting peers or
an explicit empty value require a null primary, with all candidates retained.
Every rendered translation must match its selected reference and dictionary.
Resolvable language rows, including Korean, cannot be omitted.
Each text candidate must match its decoded field, template owner and FText value.
Its role must match the source class's text-field contract.
Catalog IDs must match decoded identity fields, overrides and persistence links.
Container and visual-slot labels must follow the decoded references, category
values and complete tag-query membership. Images must match their declared source
fields. Class declarations are checked against the extraction's exact mapping.

Every mounted package needs a complete export-header inventory, including exact
superclass references. Each selected
index needs a decoded object with the same full path and class. Candidate classes
and registry UI textures cannot remain header-only; UI textures also need PNGs.
Unknown or conflicting export metadata blocks publication.

Every non-native reference needs an inspected package. Named targets must match
the complete object/outer path, case-insensitively; relevant classes and referenced
class/struct declarations, widgets, class defaults and templates also need decoded
evidence. Known unrelated classes can remain header-only. Registry aliases
prove package inspection, without rewriting object paths.

Property headers require unique, contiguous ordinal slots and valid array metadata.
Field pointers must resolve within declared property containers; native fields
retain their own paths. Base64 must be canonical. `numeric-le-base64` also requires
a supported canonical array type (for example, `UInt16Property[]`) and a decoded
byte length divisible by its element width. See [discovery](discovery.md) for the encoding.

Validation reads a private copy on disk. Later changes to the input directory
cannot change the validated payload; large images and evidence streams are not
held together in memory. Complete game coverage still requires a separate audit.

`metadata.json` records:

```json
{
  "formatVersion": 1,
  "extractorCommit": "<40 lowercase hexadecimal characters>",
  "steam": {"appId": 1808500, "depotId": 1808501, "manifestId": "<positive decimal u64>"}
}
```

The data Git commit identifies the snapshot. Its lightweight tag is
`arc-<manifestId>-<dataCommit12>`; metadata does not include its own commit or tag.
Changed snapshots push `data` and the tag atomically, without force. A rejected
push applies neither update; a concurrent writer is never overwritten.

Identical catalog, filtered resources, localization, and catalog PNG bytes keep
the existing commit, metadata and tag. Coverage, source-only edits and excluded
evidence cannot create a snapshot on their own. All localization changes remain
versioned. New run provenance stays in its logs/artifacts; identical retries
create no revision.

## Initialization

For the approved first snapshot, `--init` writes an orphan `data` commit and its
lightweight tag to the remote. It performs the same complete preview validation:

```sh
dotnet run --project src/PublishSnapshot -c Release -- \
  --init <preview-directory> <remote> <extractor-commit> <manifest-id>
```

Initialization rejects a `data` branch found by its initial check. Atomic push
and empty expected-ref leases reject conflicting creations after that check;
they never overwrite a different commit or tag. If a racing initializer creates
the identical commit, Git may safely complete its matching tag. Repeated `--init`
is rejected once `data` exists; verify an identical retry with normal publication.

## Activation

Before merging source changes, upgrade and pin the importer to a reviewed legacy
data commit and drain older importer runs. Retire the old publisher and drain its
queued/running writes too. After merging, atomically initialize `data` and its
exact lightweight tag, then validate an identical retry before switching the
importer. Verify the first import before scheduling publication. No scheduled
or production publishing workflow is enabled by this draft.
