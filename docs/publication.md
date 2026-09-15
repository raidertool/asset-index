# Snapshot publication

Activation is pending. The publisher requires an existing initialized `data`
branch containing the complete generated snapshot below and `metadata.json`.
It does not create the initial branch.

- `assets.json`: catalog rows and presentation provenance.
- `resources.json` and `images/`: every exported image resource, including unowned UI images.
- `discovery/{objects,exports,files,registry,packages}.jsonl.gz`: typed source evidence and coverage.
- `localization/<locale>.jsonl.gz`: merged localization dictionaries, including English.
- `coverage.json`: extraction counts, errors, notices, mapping hash, and discovery scope.

```sh
dotnet run --project src/PublishSnapshot -c Release -- \
  <preview-directory> <remote> <extractor-commit> <manifest-id>
```

The preview must report success without errors, have a definition for every ID,
and pass provenance, evidence-count, gzip integrity, resource-reference, and full
PNG decoding checks. Every catalog source and image resource must have object evidence;
every PNG must belong to the resource inventory. Image filenames hash their
declared resource paths, not their PNG bytes.

Every mounted package needs a complete export-header inventory. Each selected
index needs a decoded object with the same full path and class. Candidate classes
and registry UI textures cannot remain header-only; UI textures also need PNGs.
Unknown or conflicting metadata blocks publication.

Every non-native reference needs an inspected package. Named targets must match
the complete object/outer path, case-insensitively; relevant classes also need
decoded evidence. Known unrelated classes can remain header-only. Registry aliases
prove package inspection, without rewriting object paths.

Property headers require unique, contiguous ordinal slots and valid array metadata.
Field pointers must resolve within declared property containers; native fields
retain their own paths. Binary values require canonical, lossless base64.

Validation reads a private copy on disk. Later changes to the input directory
cannot change the validated payload; large images and evidence streams are not
held together in memory. Field-level text provenance, UI label choice, and
complete game/API coverage require separate audits.

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

Identical catalog, resource, discovery, localization, and PNG bytes keep the
existing commit, metadata and tag. Only metadata and coverage are excluded from
payload equality. Unowned text or resource changes therefore create a snapshot;
source-only edits with unchanged output do not. New run provenance stays in its
logs/artifacts. Retrying identical input creates no revision.

Before merging source changes, upgrade the importer, pin it to a reviewed legacy
data commit, and drain older runs. Then transfer ownership from the old publisher,
initialize and validate `data`, and switch the importer to it. Verify the first
import before scheduling publication. No scheduled or production publishing
workflow is enabled by this draft.
