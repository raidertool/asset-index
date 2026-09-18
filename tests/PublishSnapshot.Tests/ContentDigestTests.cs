using System.Text;

namespace PublishSnapshot.Tests;

public sealed class ContentDigestTests
{
    [Fact]
    public void CanonicalInventoryHasFixedOrderingAndExcludesProvenanceAndSource()
    {
        var entries = new[]
        {
            new ContentDigest.Entry("100644", new string('b', 40), "resources.json"),
            new ContentDigest.Entry("100644", new string('a', 40), "assets.json"),
            new ContentDigest.Entry("100644", new string('c', 40), "metadata.json"),
            new ContentDigest.Entry("100644", new string('d', 40), "coverage.json"),
            new ContentDigest.Entry("100644", new string('e', 40), "src/main.cs")
        };
        var canonical = "asset-index-content-v1\n100644 " + new string('a', 40) + "\tassets.json\n100644 " + new string('b', 40) + "\tresources.json\n";
        var expected = Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(canonical)));
        Assert.Equal(expected, ContentDigest.Tree(entries));
        Assert.Equal(expected, ContentDigest.Tree(entries.Reverse()));
        Assert.NotEqual(expected, ContentDigest.Tree(entries.Append(new("100644", new string('f', 40), "images/Game/New.png"))));
    }
}

public sealed partial class PublisherTests
{
    [Theory]
    [InlineData("asset_index.csv")]
    [InlineData("asset_localizations.csv")]
    public void ModifiedConvenienceCsvFailsBeforePublication(string file)
    {
        File.AppendAllText(Path.Combine(preview, file), "invented,data\n");
        var before = remoteGit.Run("show-ref");
        var error = Assert.Throws<InvalidDataException>(() => Publisher.Publish(preview, remote, NextExtractor, "456"));
        Assert.Contains("CSV differs", error.Message);
        Assert.Equal(before, remoteGit.Run("show-ref"));
    }

    [Fact]
    public void GitBlobInventoryMatchesPublishedMetadata()
    {
        var result = Publisher.Publish(preview, remote, NextExtractor, "456");
        var entries = remoteGit.Run("ls-tree", "-r", "-z", result.Commit)
            .Split('\0', StringSplitOptions.RemoveEmptyEntries).Select(row =>
            {
                var parts = row.Split('\t', 2);
                var header = parts[0].Split(' ');
                return new ContentDigest.Entry(header[0], header[2], parts[1]);
            });
        Assert.Equal(ReadMetadata(result.Commit).ContentSha256, ContentDigest.Tree(entries));
    }

    [Fact]
    public void GeneratedSymlinkCannotOverwriteSource()
    {
        var git = new Git(seed);
        git.Run("rm", "--quiet", "-r", "images");
        Directory.CreateDirectory(Path.Combine(seed, "source-images"));
        File.WriteAllText(Path.Combine(seed, "source-images", "keep.txt"), "keep");
        Directory.CreateSymbolicLink(Path.Combine(seed, "images"), "source-images");
        Commit(git);
        git.Run("push", "--quiet", "origin", "HEAD:refs/heads/main");
        var before = remoteGit.Run("show-ref");
        Assert.Throws<InvalidDataException>(() => Publisher.Publish(preview, remote, NextExtractor, "456"));
        Assert.Equal(before, remoteGit.Run("show-ref"));
    }

    [Fact]
    public void ConcurrentSourceCommitIsPreservedOnRetry()
    {
        File.WriteAllText(Path.Combine(seed, "README.md"), "Concurrent documentation");
        var git = new Git(seed);
        Commit(git);
        var competing = git.Run("rev-parse", "HEAD").Trim();
        git.Run("push", "--quiet", "origin", "HEAD:refs/heads/race-source");
        var hook = InstallHook($"unset GIT_QUARANTINE_PATH\ngit update-ref refs/heads/main {competing} {initialCommit} || exit 1\nexit 0\n");

        Assert.Throws<IOException>(() => Publisher.Publish(preview, remote, NextExtractor, "456"));
        Assert.Equal(competing, RemoteRef("refs/heads/main"));
        File.Delete(hook);
        var result = Publisher.Publish(preview, remote, NextExtractor, "456");

        Assert.Equal("Concurrent documentation", remoteGit.Run("show", result.Commit + ":README.md"));
        Assert.EndsWith(" " + competing, remoteGit.Run("rev-list", "--parents", "-n", "1", result.Commit).Trim());
    }
}
