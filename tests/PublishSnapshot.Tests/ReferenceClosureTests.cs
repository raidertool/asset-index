using System.Security.Cryptography;
using System.Text.Json.Nodes;

namespace PublishSnapshot.Tests;

public sealed partial class PublisherTests
{
    [Theory]
    [InlineData("/Game/DA_Test.Missing")]
    [InlineData("/Game/DA_Test.DA_Test:WrongParent.Leaf")]
    [InlineData("/Game/DA_Test.Other:Parent.Leaf")]
    public void MissingNamedTargetLeavesPublishedReferencesAndFilesUnchanged(string target)
    {
        AddReferenceFixture(target);
        var references = remoteGit.Run("show-ref");
        var publishedFiles = remoteGit.Run("ls-tree", "-r", "refs/heads/data");
        var inputFiles = HashFiles(preview);

        var error = Assert.Throws<InvalidDataException>(() => Publisher.Publish(preview, remote, NextExtractor, "456"));

        Assert.Contains("Named reference target is absent", error.Message);
        Assert.Contains(target, error.Message);
        Assert.Equal(references, remoteGit.Run("show-ref"));
        Assert.Equal(publishedFiles, remoteGit.Run("ls-tree", "-r", "refs/heads/data"));
        Assert.Equal(inputFiles, HashFiles(preview));
    }

    [Theory]
    [InlineData("/game/da_test.da_test:parent.leaf")]
    [InlineData("/Game/DA_Test.DA_Test:Parent.Leaf")]
    [InlineData("/Game/DA_Test.DA_Test")]
    [InlineData("/Game/DA_Test")]
    [InlineData("/script/CoreUObject.Object")]
    [InlineData("/Game/Unobserved.Unobserved:Child")]
    public void ValidAndOutOfScopeReferencesKeepTheNarrowClosureCheck(string target)
    {
        AddReferenceFixture(target);

        var result = Publisher.Publish(preview, remote, NextExtractor, "456");

        Assert.True(result.Changed);
        Assert.Equal(result.Commit, RemoteRef("refs/heads/data"));
        Assert.Equal(result.Commit, RemoteRef("refs/tags/" + result.Tag));
    }

    [Fact]
    public void ObjectPathsDifferingOnlyByCaseAreRejected()
    {
        ChangeLines("discovery/objects.jsonl.gz", rows =>
        {
            var duplicate = rows[0]!.DeepClone();
            duplicate["path"] = "/game/da_test.da_test";
            rows.Add(duplicate);
        });
        ChangeJson("coverage.json", node => node["discovery"]!["objects"] = 3);

        var error = Assert.Throws<InvalidDataException>(() => Publisher.Publish(preview, remote, NextExtractor, "456"));

        Assert.Contains("differ only by case", error.Message);
        Assert.Equal(initialCommit, RemoteRef("refs/heads/data"));
    }

    private void AddReferenceFixture(string target)
    {
        ChangeLines("discovery/objects.jsonl.gz", rows =>
        {
            foreach (var path in new[] { "/Game/DA_Test.DA_Test:Parent", "/Game/DA_Test.DA_Test:Parent.Leaf" })
            {
                var child = rows[0]!.DeepClone();
                child["path"] = path;
                rows.Add(child);
            }
            rows[0]!["properties"]!.AsArray().Add(PropertyHeader("/Properties/4", "RelatedAsset", "SoftObjectProperty"));
            rows[0]!["references"]!.AsArray().Add(new JsonObject
            {
                ["pointer"] = "/Properties/4",
                ["kind"] = "soft",
                ["role"] = "property",
                ["targetPath"] = target,
                ["isNull"] = false,
                ["package"] = null,
                ["packageIndex"] = null,
                ["exportIndex"] = null,
                ["error"] = null
            });
        });
        ChangeJson("coverage.json", node => node["discovery"]!["objects"] = 4);
        ChangeLines("discovery/packages.jsonl.gz", rows => { rows[0]!["exports"] = 3; rows[0]!["loaded"] = 3; });
    }

    private static string[] HashFiles(string directory) => Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories)
        .Order(StringComparer.Ordinal)
        .Select(path => Path.GetRelativePath(directory, path) + ":" + Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))))
        .ToArray();
}
