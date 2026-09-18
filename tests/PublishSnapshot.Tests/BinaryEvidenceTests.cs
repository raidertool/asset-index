using System.Text.Json.Nodes;

namespace PublishSnapshot.Tests;

public sealed partial class PublisherTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(256)]
    public void BinaryEvidenceIsValidatedWithoutChangingItsBytes(int length)
    {
        var bytes = Enumerable.Range(0, length).Select(index => (byte)index).ToArray();
        AddEncodedEvidence(Convert.ToBase64String(bytes));
        AssertEvidenceValidatedAndUnchanged();
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
        AddEncodedEvidence(encoded);
        AssertEvidenceRejectedBeforeGit();
    }

    private void AssertEvidenceValidatedAndUnchanged()
    {
        var before = HashFiles(preview);
        var result = Publisher.Publish(preview, remote, NextExtractor, "456");

        Assert.True(result.Changed);
        Assert.Equal(before, HashFiles(preview));
        Assert.DoesNotContain("discovery/", remoteGit.Run("ls-tree", "-r", "--name-only", result.Commit));
        Assert.False(Publisher.Publish(preview, remote, NextExtractor, "456").Changed);
    }

    private void AssertEvidenceRejectedBeforeGit()
    {
        var references = remoteGit.Run("show-ref");
        var publishedFiles = remoteGit.Run("ls-tree", "-r", "refs/heads/data");
        var input = HashFiles(preview);

        Assert.Throws<InvalidDataException>(() => Publisher.Publish(preview,
            Path.Combine(root, "nonexistent-remote.git"), NextExtractor, "456"));

        Assert.Equal(references, remoteGit.Run("show-ref"));
        Assert.Equal(publishedFiles, remoteGit.Run("ls-tree", "-r", "refs/heads/data"));
        Assert.Equal(input, HashFiles(preview));
    }

    private void AddEncodedEvidence(string? encoded, string type = "ByteProperty[]", string kind = "binary-base64") => ChangeLines("discovery/objects.jsonl.gz", rows =>
    {
        rows[0]!["properties"]!.AsArray().Add(PropertyHeader("/Properties/4", "Data", "ArrayProperty"));
        rows[0]!["values"]!.AsArray().Add(new JsonObject
        {
            ["pointer"] = "/Properties/4",
            ["type"] = type,
            ["kind"] = kind,
            ["value"] = encoded
        });
    });
}
