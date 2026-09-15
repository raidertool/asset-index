using System.Text.Json.Nodes;

namespace PublishSnapshot.Tests;

public sealed partial class PublisherTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(256)]
    public void BinaryEvidencePublishesWithoutChangingItsBytes(int length)
    {
        var bytes = Enumerable.Range(0, length).Select(index => (byte)index).ToArray();
        AddBinaryEvidence(Convert.ToBase64String(bytes));
        var before = HashFiles(preview);

        var result = Publisher.Publish(preview, remote, NextExtractor, "456");

        Assert.True(result.Changed);
        Assert.Equal(before, HashFiles(preview));
        Assert.Equal(remoteGit.Run("hash-object", Path.Combine(preview, "discovery/objects.jsonl.gz")).Trim(),
            remoteGit.Run("rev-parse", result.Commit + ":discovery/objects.jsonl.gz").Trim());
        Assert.False(Publisher.Publish(preview, remote, NextExtractor, "456").Changed);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("!")]
    [InlineData("AA")]
    [InlineData("AA==\n")]
    [InlineData("A A=")]
    [InlineData("====")]
    [InlineData("AA==AAAA")]
    [InlineData("AB==")]
    [InlineData("AAB=")]
    public void InvalidBinaryEvidenceCannotChangePublishedReferences(string? encoded)
    {
        AddBinaryEvidence(encoded);
        var references = remoteGit.Run("show-ref");
        var publishedFiles = remoteGit.Run("ls-tree", "-r", "refs/heads/data");
        var input = HashFiles(preview);

        Assert.Throws<InvalidDataException>(() => Publisher.Publish(preview,
            Path.Combine(root, "nonexistent-remote.git"), NextExtractor, "456"));

        Assert.Equal(references, remoteGit.Run("show-ref"));
        Assert.Equal(publishedFiles, remoteGit.Run("ls-tree", "-r", "refs/heads/data"));
        Assert.Equal(input, HashFiles(preview));
    }

    private void AddBinaryEvidence(string? encoded) => ChangeLines("discovery/objects.jsonl.gz", rows =>
    {
        rows[0]!["properties"]!.AsArray().Add(PropertyHeader("/Properties/4", "Data", "ArrayProperty"));
        rows[0]!["values"]!.AsArray().Add(new JsonObject
        {
            ["pointer"] = "/Properties/4",
            ["type"] = "ByteProperty[]",
            ["kind"] = "binary-base64",
            ["value"] = encoded
        });
    });
}
