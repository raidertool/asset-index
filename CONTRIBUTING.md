# Contributing

Keep changes small and easy to review. One extractor and one test project are
enough; add abstractions when a concrete feature needs them.

## Fix missing assets

1. Link the relevant issue and identify the asset ID, name, or game path.
2. Trace the value in the game files. Avoid guessing names or image associations.
3. Add an offline regression test with a small synthetic input where practical.
4. Run the tests from [README.md](README.md#contribute).
5. If you can test with game files, include the game build and relevant coverage
   results in your PR. Say clearly when you could not run that check.

Coverage checks include XP imagery, missing localized names, shared Scrappy imagery,
and IDs reported in [issues](https://github.com/raidertool/asset-index/issues).
Existing data is useful evidence, but matching it is not proof of completeness.

## Update the mapping

Replace `mappings/ArcRaiders.usmap` and open a PR. Include where it came from and
the game build you tested, or say compatibility is untested. No custom mapping
diff or metadata file is required.

## Test with Steam files

Use an installed game directory or mount SteamDepotFS with your own credentials
as described in the [README](README.md#run-locally). Keep credentials in your
local environment; do not put them in commands committed to the repository,
issues, fixtures, or test output. Keep game files and depot caches outside Git.

PR CI runs offline. The public repository also defines a manual
[preview workflow](README.md#manual-preview-job) for reviewed maintainer runs.
Complete maintainer review of the workflow and dependency pin before configuring
credentials or dispatching it. Contributors can test locally with their own
credentials; no maintainer secrets are shared.

## Review the output

Use a new directory under `.work/` for each preview. Inspect `coverage.json` and
the affected rows in `assets.json`; check image references against decoded files.
Report extraction failures instead of filling gaps from an older snapshot.

Submit source, tests, or mapping changes. Leave generated preview files out of
the PR. Publication and database import are separate work; this preview does
not establish the final consumer schema.
