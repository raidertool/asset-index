#!/usr/bin/env python3
"""Release policy and immutable-tag tests; all remotes are local directories."""
import importlib.util
from pathlib import Path
import subprocess
import tempfile
import unittest

SPEC = importlib.util.spec_from_file_location("release", Path(__file__).resolve().parents[2] / "scripts/release/version.py")
release = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(release)


class ReleaseTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.addCleanup(self.temp.cleanup)
        self.repo = Path(self.temp.name) / "repo"
        self.repo.mkdir()
        self.git("init", "-q", "-b", "main")
        self.git("config", "user.name", "Release test")
        self.git("config", "user.email", "release@example.invalid")
        self.commit("src/AssetIndex/Test.cs", "chore: baseline")
        self.git("tag", release.LEGACY_TAG)

    def git(self, *args):
        return release.git(self.repo, *args)

    def commit(self, path, message):
        file = self.repo / path
        file.parent.mkdir(parents=True, exist_ok=True)
        file.write_text(file.read_text() + "\nchange" if file.exists() else "change")
        self.git("add", path)
        self.git("commit", "-q", "-m", message)
        return self.git("rev-parse", "HEAD")

    def remote(self):
        remote = Path(self.temp.name) / "remote.git"
        subprocess.run(["git", "clone", "--bare", "--quiet", str(self.repo), str(remote)], check=True)
        self.git("remote", "add", "origin", str(remote))
        return remote

    def test_generated_docs_tests_and_release_tooling_do_not_bump(self):
        for path in ["assets.json", "asset_index.csv", "images/T_Icon.png", "metadata.json", "docs/notes.md",
                     "src/README.md", "tests/Case.cs", "scripts/release/version.py", ".github/workflows/release.yml",
                     ".github/workflows/test.yml", "scripts/ci/README.md"]:
            self.commit(path, "feat!: change generated data or documentation")
        self.assertIsNone(release.plan(self.repo)["tag"])

    def test_source_severity_and_footer(self):
        self.commit("src/AssetIndex/Test.cs", "fix(text): preserve missing translations")
        self.assertEqual(release.plan(self.repo)["tag"], "exfil-v0.13.3")
        self.commit("mappings/ArcRaiders.usmap", "feat: update mapping")
        self.assertEqual(release.plan(self.repo)["tag"], "exfil-v0.14.0")
        self.commit("src/PublishSnapshot/Test.cs", "refactor: simplify output\n\nBREAKING CHANGE: update the format")
        self.assertEqual(release.plan(self.repo)["tag"], "exfil-v1.0.0")

    def test_nonconventional_and_chore_do_not_bump(self):
        self.commit("src/AssetIndex/Test.cs", "Update parser")
        self.commit("global.json", "chore: update SDK")
        self.assertIsNone(release.plan(self.repo)["tag"])

    def test_empty_message_does_not_block_later_source_releases(self):
        self.git("commit", "--allow-empty", "--allow-empty-message", "-qm", "")
        self.assertIsNone(release.plan(self.repo)["tag"])
        self.commit("src/AssetIndex/Test.cs", "fix: preserve a source correction")
        self.assertEqual(release.plan(self.repo)["tag"], "exfil-v0.13.3")

    def test_changed_paths_preserve_unicode_and_embedded_whitespace(self):
        for patch, path in enumerate(["src/번역.cs", "src/Tab\tName.cs", "src/Line\nName.cs"], start=3):
            with self.subTest(path=path):
                commit = self.commit(path, "fix: preserve source changes")
                self.assertEqual(release.changed_paths(self.repo, commit), [path])
                version = f"exfil-v0.13.{patch}"
                self.assertEqual(release.plan(self.repo)["tag"], version)
                self.git("tag", version)

    def test_runtime_extraction_orchestration_counts_as_software(self):
        self.commit(".github/workflows/extract.yml", "fix: correct Steam mount timeout")
        self.assertEqual(release.plan(self.repo)["tag"], "exfil-v0.13.3")
        self.git("tag", "exfil-v0.13.3")
        self.commit("scripts/ci/prepare-runner-disk.sh", "fix: retain enough extraction disk space")
        self.assertEqual(release.plan(self.repo)["tag"], "exfil-v0.13.4")

    def test_actual_paths_override_commit_scope(self):
        self.commit("docs/notes.md", "fix(extractor): typo")
        self.assertIsNone(release.plan(self.repo)["tag"])
        self.commit("global.json", "fix(docs): supported SDK")
        self.assertEqual(release.plan(self.repo)["tag"], "exfil-v0.13.3")

    def test_deleted_source_counts(self):
        self.git("rm", "-q", "src/AssetIndex/Test.cs")
        self.git("commit", "-q", "-m", "feat!: remove old entry point")
        self.assertEqual(release.plan(self.repo)["tag"], "exfil-v1.0.0")

    def test_legacy_data_branch_need_not_be_ancestor(self):
        self.git("tag", "-d", release.LEGACY_TAG)
        self.git("checkout", "-q", "-b", "legacy")
        self.commit("asset_index.csv", "chore: old data snapshot")
        self.git("tag", release.LEGACY_TAG)
        self.git("checkout", "-q", "main")
        self.commit("src/AssetIndex/Test.cs", "feat: new extractor")
        self.assertEqual(release.plan(self.repo)["tag"], "exfil-v0.14.0")

    def test_merge_history_counts_feature_commit(self):
        self.git("checkout", "-q", "-b", "feature")
        self.commit("src/New.cs", "feat: supported relationship")
        self.git("checkout", "-q", "main")
        self.git("merge", "--no-ff", "-q", "feature", "-m", "Merge pull request")
        self.assertEqual(release.plan(self.repo)["tag"], "exfil-v0.14.0")

    def test_source_tag_namespace_and_highest_semver(self):
        self.git("tag", "exfil-v1.9.0")
        self.git("tag", "exfil-v1.10.0")
        self.git("tag", "v99.0.0")
        self.git("tag", "arc-999-exfil-v99.0.0")
        self.git("tag", "exfil-v01.99.0")
        self.commit("src/AssetIndex/Test.cs", "fix: source change")
        self.assertEqual(release.plan(self.repo)["tag"], "exfil-v1.10.1")

    def test_rerun_and_stale_target_do_not_create_versions(self):
        old = self.git("rev-parse", "HEAD")
        self.commit("src/AssetIndex/Test.cs", "feat: new feature")
        self.git("tag", "exfil-v0.14.0")
        self.assertIsNone(release.plan(self.repo)["tag"])
        self.assertIsNone(release.plan(self.repo, old)["tag"])
        self.commit("assets.json", "fix: regenerated snapshot")
        self.assertIsNone(release.plan(self.repo)["tag"])

    def test_divergent_source_tag_rejected(self):
        self.git("checkout", "-q", "-b", "other")
        self.commit("src/Other.cs", "feat: unrelated feature")
        self.git("tag", "exfil-v0.14.0")
        self.git("checkout", "-q", "main")
        self.commit("src/Main.cs", "fix: source change")
        with self.assertRaisesRegex(RuntimeError, "outside the target ancestry"):
            release.plan(self.repo)

    def test_publish_only_tag_preserves_main_and_reruns(self):
        target = self.commit("src/AssetIndex/Test.cs", "fix: source change")
        remote = self.remote()
        result = release.publish(self.repo, target, "origin")
        self.assertEqual(result["tag"], "exfil-v0.13.3")
        self.assertEqual(release.git(remote, "rev-parse", "refs/heads/main"), target)
        self.assertEqual(release.git(remote, "rev-parse", "refs/tags/exfil-v0.13.3"), target)
        self.assertIsNone(release.publish(self.repo, target, "origin")["tag"])
        self.assertEqual(release.git(remote, "rev-parse", release.LEGACY_TAG), self.git("rev-parse", release.LEGACY_TAG + "^{commit}"))

    def test_unmerged_target_rejected(self):
        self.remote()
        target = self.commit("src/AssetIndex/Test.cs", "feat: unmerged source")
        with self.assertRaisesRegex(RuntimeError, "not on remote main"):
            release.publish(self.repo, target, "origin")

    def test_conflicting_remote_tag_is_not_overwritten(self):
        target = self.commit("src/AssetIndex/Test.cs", "fix: source change")
        remote = self.remote()
        original = release.plan
        def raced_plan(repo, commit):
            result = original(repo, commit)
            release.git(remote, "tag", result["tag"], release.LEGACY_TAG)
            return result
        from unittest.mock import patch
        with patch.object(release, "plan", side_effect=raced_plan):
            with self.assertRaises(subprocess.CalledProcessError):
                release.publish(self.repo, target, "origin")
        self.assertEqual(release.git(remote, "rev-parse", "refs/tags/exfil-v0.13.3"), self.git("rev-parse", release.LEGACY_TAG))


if __name__ == "__main__":
    unittest.main()
