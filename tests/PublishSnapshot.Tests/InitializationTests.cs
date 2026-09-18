namespace PublishSnapshot.Tests;

public sealed partial class PublisherTests
{
    [Fact]
    public void FirstPublicationReplacesLegacyGeneratedFilesAndPreservesDataDocumentation()
    {
        var git = new Git(seed);
        git.Run("rm", "--quiet", "-r", ".");
        File.WriteAllText(Path.Combine(seed, "README.md"), "Dataset documentation");
        File.WriteAllText(Path.Combine(seed, "LICENSE"), "Dataset license");
        Directory.CreateDirectory(Path.Combine(seed, "images"));
        File.WriteAllText(Path.Combine(seed, "images", "Old_asset.png"), "old image");
        File.WriteAllText(Path.Combine(seed, "asset_index.csv"), "old schema");
        File.WriteAllText(Path.Combine(seed, "schema.json"), "old schema");
        Commit(git);
        git.Run("push", "--quiet", "origin", "HEAD:refs/heads/data");
        var parent = RemoteRef("refs/heads/data");

        var result = Publisher.Publish(preview, remote, NextExtractor, "456");

        Assert.Equal(result.Commit + " " + parent, remoteGit.Run("rev-list", "--parents", "-n", "1", result.Commit).Trim());
        Assert.Equal("Dataset documentation", remoteGit.Run("show", result.Commit + ":README.md"));
        Assert.Equal("Dataset license", remoteGit.Run("show", result.Commit + ":LICENSE"));
        Assert.Equal(initialSourceCommit, RemoteRef("refs/heads/main"));
        var paths = remoteGit.Run("ls-tree", "-r", "--name-only", result.Commit);
        Assert.DoesNotContain("Old_asset.png", paths);
        Assert.DoesNotContain("schema.json", paths);
        Assert.DoesNotContain("discovery/", paths);
        Assert.DoesNotContain("src/", paths);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void MissingDataBranchCannotBeInitializedByPublication(bool fromExport)
    {
        remoteGit.Run("update-ref", "-d", "refs/heads/data");
        var before = remoteGit.Run("show-ref");
        if (fromExport)
        {
            var (directory, _) = Handoff();
            Assert.Throws<IOException>(() => Publisher.PublishExport(directory, remote, NextExtractor,
                "456", HandoffDigest(directory), "missing"));
        }
        else
            Assert.Throws<IOException>(() => Publisher.Publish(preview, remote, NextExtractor, "456"));
        Assert.Equal(before, remoteGit.Run("show-ref"));
    }

    [Fact]
    public void ExportValidatesPrivateEvidenceAndWritesOnlySafeFiles()
    {
        ChangeJson("coverage.json", report =>
        {
            report["notices"]!.AsArray().Add(new System.Text.Json.Nodes.JsonObject
            { ["stage"] = "private-stage", ["path"] = "private-path", ["message"] = "private-investigation" });
            report["discovery"]!["nativeScope"] = "private-method";
        });
        var output = Path.Combine(root, "safe-export");
        var before = HashFiles(preview);
        var metadata = Publisher.Export(preview, output, NextExtractor, "456");
        var files = Directory.GetFiles(output, "*", SearchOption.AllDirectories)
            .ToDictionary(path => Path.GetRelativePath(output, path).Replace('\\', '/'), SnapshotFile.Read);

        Assert.Equal(metadata.ContentSha256, ContentDigest.Files(files));
        Assert.Equal(DataSnapshot.Required.Append(Image).Append("metadata.json").Order(), files.Keys.Order());
        Assert.DoesNotContain("private-", File.ReadAllText(Path.Combine(output, "coverage.json")));
        Assert.Equal(before, HashFiles(preview));
        Assert.Throws<InvalidDataException>(() => Publisher.Export(preview, output, NextExtractor, "456"));
        Assert.Equal(0, Program.Main(["--help"]));
        Assert.Equal(2, Program.Main(["--init", preview, remote, NextExtractor, "456"]));
    }

    [Fact]
    public void FailedExportLeavesNoOutputAndCanBeRetried()
    {
        var output = Path.Combine(root, "safe-export");
        Corrupt("incomplete");
        Assert.Throws<InvalidDataException>(() => Publisher.Export(preview, output, NextExtractor, "456"));
        Assert.False(Path.Exists(output));
        ChangeJson("coverage.json", report => report["status"] = "succeeded");
        Assert.Equal(0, Program.Main(["--export", preview, output, NextExtractor, "456"]));
        Assert.True(File.Exists(Path.Combine(output, "metadata.json")));
    }

    [Fact]
    public void ConflictingReleaseTagCannotMoveData()
    {
        using var captured = Preview.Read(preview);
        using var snapshot = DataSnapshot.Create(captured);
        var tag = Publisher.Tag("456", ContentDigest.Files(snapshot.Files));
        remoteGit.Run("update-ref", "refs/tags/" + tag, initialCommit);
        var before = remoteGit.Run("show-ref");

        Assert.Throws<InvalidDataException>(() => Publisher.Publish(preview, remote, NextExtractor, "456"));

        Assert.Equal(before, remoteGit.Run("show-ref"));
    }

    [Fact]
    public void RetryCannotRestoreAnOlderReleaseOverNewerData()
    {
        Publisher.Publish(preview, remote, NextExtractor, "456");
        WritePreview(preview, "Initial name");
        var before = remoteGit.Run("show-ref");

        Assert.Throws<InvalidDataException>(() => Publisher.Publish(preview, remote, InitialExtractor, "123"));

        Assert.Equal(before, remoteGit.Run("show-ref"));
    }
}
