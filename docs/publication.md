# Snapshot publication

Activation is pending. The publisher requires an existing initialized `data`
branch containing the complete generated snapshot below and `metadata.json`.
It does not create the initial branch.

- `assets.json`: catalog rows and presentation provenance.
- `resources.json` and `images/`: every exported texture, including unowned UI images.
- `discovery/{objects,registry,packages}.jsonl.gz`: typed source evidence and coverage.
- `localization/<locale>.jsonl.gz`: merged localization dictionaries, including English.
- `coverage.json`: extraction counts, errors, notices, mapping hash, and discovery scope.

```sh
dotnet run --project src/PublishSnapshot -c Release -- \
  <preview-directory> <remote> <extractor-commit> <manifest-id>
```

The preview must report success without errors, have a definition for every ID,
and pass provenance, evidence-count, gzip integrity, resource-reference, and full
PNG decoding checks. Every catalog source and texture must have object evidence;
every PNG must belong to the resource inventory. Image filenames hash their
declared resource paths, not their PNG bytes.

Validation reads a private copy on disk. Later changes to the input directory
cannot change the validated payload; large images and evidence streams are not
held together in memory. These checks verify structure and consistency, not
whether the extractor found every game object or chose the intended UI label.

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

Before activation, initialize and validate the data branch, transfer ownership
from the old publisher, and resolve importer overlap/ordering. No scheduled or
production publishing workflow is enabled by this draft.
