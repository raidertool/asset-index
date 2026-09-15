# Snapshot publication

Activation is pending. The publisher requires an existing initialized `data`
branch containing only `assets.json`, `coverage.json`, `metadata.json`, and PNGs
under `images/`. It does not create the initial branch.

```sh
dotnet run --project src/PublishSnapshot -c Release -- \
  <preview-directory> <remote> <extractor-commit> <manifest-id>
```

The preview must report success without diagnostics, have a definition for every
ID, and pass row, image-reference, filename-hash, and full PNG decoding checks.
Image filenames hash their declared texture paths, not their PNG bytes.

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

Identical `assets.json` and PNG bytes keep the existing commit, metadata and tag,
even when the extractor commit, manifest or coverage report differs. New run
provenance stays in its logs/artifacts. Retrying identical input creates no revision.

Before activation, initialize and validate the data branch, transfer ownership
from the old publisher, and resolve importer overlap/ordering. No scheduled or
production publishing workflow is enabled by this draft.
