using System.Text.Json;

namespace PublishSnapshot.Tests;

public sealed partial class PublisherTests
{
    [Fact]
    public void InitializationCreatesAnOrphanProjectedSnapshotAndNormalRetryIsUnchanged()
    {
        remoteGit.Run("update-ref", "-d", "refs/heads/data");
        var unowned = AddUnownedImage("/Game/T_RegistryOnly.T_RegistryOnly", "Texture2D");
        var input = HashFiles(preview);
        var seedReferences = new Git(seed).Run("show-ref");

        Assert.Equal(0, Program.Main(["--init", preview, remote, NextExtractor, "456"]));

        var commit = RemoteRef("refs/heads/data");
        var tag = Publisher.Tag("456", commit);
        Assert.Equal(commit, RemoteRef("refs/tags/" + tag));
        Assert.Equal("commit", remoteGit.Run("cat-file", "-t", "refs/tags/" + tag).Trim());
        Assert.Equal(commit, remoteGit.Run("rev-list", "--parents", "-n", "1", commit).Trim());
        Assert.Equal(initialCommit, RemoteRef("refs/heads/main"));
        Assert.Equal(DataSnapshot.Required.Concat([Image, unowned, "metadata.json"]).Order(),
            remoteGit.Run("ls-tree", "-r", "--name-only", commit).Split('\n', StringSplitOptions.RemoveEmptyEntries).Order());
        Assert.Equal(File.ReadAllText(Path.Combine(preview, "assets.json")), remoteGit.Run("show", commit + ":assets.json"));
        using var metadata = JsonDocument.Parse(remoteGit.Run("show", commit + ":metadata.json"));
        Assert.Equal(NextExtractor, metadata.RootElement.GetProperty("extractorCommit").GetString());
        Assert.Equal("456", metadata.RootElement.GetProperty("steam").GetProperty("manifestId").GetString());
        Assert.Equal(input, HashFiles(preview));
        Assert.Equal(seedReferences, new Git(seed).Run("show-ref"));

        var references = remoteGit.Run("show-ref");
        var retry = Publisher.Publish(preview, remote, InitialExtractor, "789");
        Assert.False(retry.Changed);
        Assert.Equal(commit, retry.Commit);
        Assert.Equal(tag, retry.Tag);
        Assert.Throws<InvalidDataException>(() => Publisher.Initialize(preview, remote, NextExtractor, "456"));
        Assert.Equal(references, remoteGit.Run("show-ref"));
    }

    [Fact]
    public void InitializationRefusesAnObservedExistingDataBranch()
    {
        var references = remoteGit.Run("show-ref");

        var error = Assert.Throws<InvalidDataException>(() => Publisher.Initialize(preview, remote, NextExtractor, "456"));

        Assert.Contains("already exists", error.Message);
        Assert.Equal(references, remoteGit.Run("show-ref"));
    }

    [Fact]
    public void InitializationValidatesTheFullPreviewBeforeAnyGitAccess()
    {
        Corrupt("incomplete");
        var references = remoteGit.Run("show-ref");
        var input = HashFiles(preview);

        var error = Assert.Throws<InvalidDataException>(() => Publisher.Initialize(preview,
            Path.Combine(root, "nonexistent-remote"), NextExtractor, "456"));

        Assert.Equal("Preview is incomplete.", error.Message);
        Assert.Equal(references, remoteGit.Run("show-ref"));
        Assert.Equal(input, HashFiles(preview));
    }

    [Fact]
    public void OrdinaryPublicationNeverInitializesAMissingDataBranch()
    {
        remoteGit.Run("update-ref", "-d", "refs/heads/data");
        var references = remoteGit.Run("show-ref");

        Assert.Throws<IOException>(() => Publisher.Publish(preview, remote, NextExtractor, "456"));

        Assert.Equal(references, remoteGit.Run("show-ref"));
    }

    [Theory]
    [InlineData("refs/heads/data")]
    [InlineData("refs/tags/*")]
    public void InitializationRefRejectionIsAtomicAndSameInputCanBeRetried(string rejected)
    {
        remoteGit.Run("update-ref", "-d", "refs/heads/data");
        var hook = InstallHook($"case \"$1\" in {rejected}) exit 1 ;; esac\nexit 0\n", "update");
        var references = remoteGit.Run("show-ref");
        var seedReferences = new Git(seed).Run("show-ref");

        Assert.Throws<IOException>(() => Publisher.Initialize(preview, remote, NextExtractor, "456"));

        Assert.Equal(references, remoteGit.Run("show-ref"));
        Assert.Equal(seedReferences, new Git(seed).Run("show-ref"));
        File.Delete(hook);
        var retry = Publisher.Initialize(preview, remote, NextExtractor, "456");
        Assert.Equal(retry.Commit, RemoteRef("refs/heads/data"));
        Assert.Equal(retry.Commit, RemoteRef("refs/tags/" + retry.Tag));
    }

    [Fact]
    public void InitializationRejectsADataCreationAfterPushAdvertisement()
    {
        remoteGit.Run("update-ref", "-d", "refs/heads/data");
        var tags = remoteGit.Run("show-ref", "--tags");
        InstallHook($"unset GIT_QUARANTINE_PATH\ngit update-ref refs/heads/data {initialCommit} {new string('0', 40)} || exit 1\nexit 0\n");

        Assert.Throws<IOException>(() => Publisher.Initialize(preview, remote, NextExtractor, "456"));

        Assert.Equal(initialCommit, RemoteRef("refs/heads/data"));
        Assert.Equal(initialCommit, RemoteRef("refs/heads/main"));
        Assert.Equal(tags, remoteGit.Run("show-ref", "--tags"));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void InitializationRejectsConflictingRefsCreatedAfterItsPrecheck(bool tag)
    {
        remoteGit.Run("update-ref", "-d", "refs/heads/data");
        var reference = tag ? "refs/tags/arc-456-$(git rev-parse HEAD | cut -c1-12)" : "refs/heads/data";
        WithInitializationRace($"git --git-dir=\"$remote\" update-ref {reference} {initialCommit}\n", () =>
            Assert.Throws<IOException>(() => Publisher.Initialize(preview, remote, NextExtractor, "456")));

        Assert.True(File.Exists(Path.Combine(remote, "race-commit")));
        Assert.Equal(initialCommit, RemoteRef("refs/heads/main"));
        if (tag)
        {
            var commit = File.ReadAllText(Path.Combine(remote, "race-commit")).Trim();
            Assert.Equal(initialCommit, RemoteRef("refs/tags/" + Publisher.Tag("456", commit)));
            Assert.Equal("", remoteGit.Run("for-each-ref", "--format=%(refname)", "refs/heads/data"));
        }
        else
        {
            Assert.Equal(initialCommit, RemoteRef("refs/heads/data"));
            Assert.Equal(Publisher.Tag("123", initialCommit), remoteGit.Run("tag", "--list").Trim());
        }
    }

    [Fact]
    public void InitializationSafelyCompletesATagWhenARacerCreatesTheIdenticalCommit()
    {
        remoteGit.Run("update-ref", "-d", "refs/heads/data");
        Publication? result = null;
        WithInitializationRace("git push --quiet origin HEAD:refs/heads/data\n", () =>
            result = Publisher.Initialize(preview, remote, NextExtractor, "456"));

        Assert.NotNull(result);
        Assert.Equal(result.Commit, File.ReadAllText(Path.Combine(remote, "race-commit")).Trim());
        Assert.Equal(result.Commit, RemoteRef("refs/heads/data"));
        Assert.Equal(result.Commit, RemoteRef("refs/tags/" + result.Tag));
        Assert.Equal(initialCommit, RemoteRef("refs/heads/main"));
    }

    private void WithInitializationRace(string mutation, Action action)
    {
        var templates = Path.Combine(root, "init-template");
        Directory.CreateDirectory(Path.Combine(templates, "hooks"));
        var hook = Path.Combine(templates, "hooks", "post-commit");
        File.WriteAllText(hook, "#!/bin/sh\nset -e\nremote=$(git remote get-url origin)\n" + mutation +
            "git rev-parse HEAD > \"$remote/race-commit\"\n");
        if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(hook, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        // All publisher cases share this xUnit class and run serially. Only the
        // disposable publisher repository receives this temporary template.
        var previous = Environment.GetEnvironmentVariable("GIT_TEMPLATE_DIR");
        try
        {
            Environment.SetEnvironmentVariable("GIT_TEMPLATE_DIR", templates);
            action();
        }
        finally { Environment.SetEnvironmentVariable("GIT_TEMPLATE_DIR", previous); }
    }
}
