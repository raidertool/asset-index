using System.Text.Json.Nodes;

namespace PublishSnapshot.Tests;

public sealed partial class PublisherTests
{
    [Theory]
    [InlineData("unread-unindexed")]
    [InlineData("missing-registry-package")]
    [InlineData("duplicate-physical-path")]
    [InlineData("duplicate-registry-alias")]
    [InlineData("invented-registry-alias")]
    [InlineData("read-outside-inventory")]
    public void InvalidMountedInputCoverageLeavesPublishedDataUnchanged(string mutation)
    {
        switch (mutation)
        {
            case "unread-unindexed": AddUnindexedPackage("PioneerGame/Content/Unknown.uasset", loaded: false); break;
            case "missing-registry-package": ChangeLines("discovery/files.jsonl.gz", rows => rows.RemoveAt(0)); break;
            case "duplicate-physical-path":
                ChangeLines("discovery/files.jsonl.gz", rows =>
                {
                    var duplicate = rows[0]!.DeepClone();
                    duplicate["path"] = duplicate["path"]!.GetValue<string>().ToUpperInvariant();
                    rows.Add(duplicate);
                }); break;
            case "duplicate-registry-alias": ChangeLines("discovery/files.jsonl.gz", rows => rows[1]!["registryPackages"]!.AsArray().Add("/game/da_test")); break;
            case "invented-registry-alias": ChangeLines("discovery/files.jsonl.gz", rows => rows[0]!["registryPackages"]!.AsArray().Add("/Game/Invented")); break;
            case "read-outside-inventory": ChangeLines("discovery/packages.jsonl.gz", rows => rows[0]!["path"] = "PioneerGame/Content/Other.uasset"); break;
            default: throw new ArgumentOutOfRangeException(nameof(mutation));
        }
        var references = remoteGit.Run("show-ref");
        var tree = remoteGit.Run("ls-tree", "-r", "refs/heads/data");
        var inputs = HashFiles(preview);

        Assert.Throws<InvalidDataException>(() => Publisher.Publish(preview, remote, NextExtractor, "456"));

        Assert.Equal(references, remoteGit.Run("show-ref"));
        Assert.Equal(tree, remoteGit.Run("ls-tree", "-r", "refs/heads/data"));
        Assert.Equal(inputs, HashFiles(preview));
    }

    [Fact]
    public void MountedPathsAndRegistryAliasesMatchCaseInsensitively()
    {
        ChangeLines("discovery/files.jsonl.gz", rows =>
        {
            rows[0]!["path"] = "PIONEERGAME/CONTENT/DA_TEST.UASSET";
            rows[0]!["registryPackages"]![0] = "/game/da_test";
        });

        Assert.True(Publisher.Publish(preview, remote, NextExtractor, "456").Changed);
    }

    [Fact]
    public void AnUnindexedInputWithACompletedPackageReadIsAccepted()
    {
        AddUnindexedPackage("PioneerGame/Content/Unknown.uasset", loaded: true);

        Assert.True(Publisher.Publish(preview, remote, NextExtractor, "456").Changed);
    }

    private void AddUnindexedPackage(string path, bool loaded, int exports = 0)
    {
        ChangeLines("discovery/files.jsonl.gz", rows => rows.Add(new JsonObject { ["path"] = path, ["registryPackages"] = new JsonArray() }));
        if (!loaded) return;
        ChangeLines("discovery/packages.jsonl.gz", rows => rows.Add(new JsonObject
        {
            ["path"] = path,
            ["name"] = "/Game/" + Path.GetFileNameWithoutExtension(path),
            ["reason"] = "unindexed",
            ["status"] = "succeeded",
            ["exports"] = exports,
            ["selected"] = new JsonArray(Enumerable.Range(0, exports).Select(value => (JsonNode?)JsonValue.Create(value)).ToArray()),
            ["decoded"] = new JsonArray(Enumerable.Range(0, exports).Select(value => (JsonNode?)JsonValue.Create(value)).ToArray())
        }));
        ChangeJson("coverage.json", node =>
        {
            node["candidates"] = node["candidates"]!.GetValue<int>() + 1;
            node["loaded"] = node["loaded"]!.GetValue<int>() + 1;
        });
    }
}
