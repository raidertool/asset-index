using System.Text.Json.Nodes;

namespace PublishSnapshot.Tests;

public sealed partial class PublisherTests
{
    [Theory]
    [InlineData("missing-header")]
    [InlineData("duplicate-index")]
    [InlineData("duplicate-path")]
    [InlineData("wrong-package")]
    [InlineData("wrong-class")]
    [InlineData("incomplete-ancestry")]
    [InlineData("unknown-header")]
    [InlineData("header-error")]
    [InlineData("out-of-range-index")]
    [InlineData("unselected-object")]
    [InlineData("undecoded-selection")]
    [InlineData("duplicate-selection")]
    public void InvalidExportAccountingLeavesPublishedFilesAndReferencesUnchanged(string mutation)
    {
        switch (mutation)
        {
            case "missing-header": ChangeLines("discovery/exports.jsonl.gz", rows => rows.RemoveAt(0)); break;
            case "duplicate-index": ChangeLines("discovery/exports.jsonl.gz", rows => rows.Add(rows[0]!.DeepClone())); break;
            case "duplicate-path": AddHeaderSibling("PersistenceDataAsset", ["DataAsset", "Object"], "/game/da_test.da_test"); break;
            case "wrong-package": ChangeLines("discovery/exports.jsonl.gz", rows => rows[0]!["path"] = "/Game/Other.DA_Test"); break;
            case "wrong-class": ChangeLines("discovery/objects.jsonl.gz", rows => rows[0]!["class"] = "Actor"); break;
            case "incomplete-ancestry": ChangeLines("discovery/exports.jsonl.gz", rows => rows[0]!["ancestry"] = new JsonArray("PersistenceDataAsset")); break;
            case "unknown-header": ChangeLines("discovery/exports.jsonl.gz", rows => rows[0]!["ancestryComplete"] = false); break;
            case "header-error": ChangeLines("discovery/exports.jsonl.gz", rows => rows[0]!["error"] = "Superclass import missing"); break;
            case "out-of-range-index": ChangeLines("discovery/packages.jsonl.gz", rows => rows[0]!["selected"] = new JsonArray(1)); break;
            case "unselected-object":
                ChangeLines("discovery/packages.jsonl.gz", rows =>
                {
                    rows[1]!["selected"] = new JsonArray();
                    rows[1]!["decoded"] = new JsonArray();
                }); break;
            case "undecoded-selection": ChangeLines("discovery/packages.jsonl.gz", rows => rows[0]!["decoded"] = new JsonArray()); break;
            case "duplicate-selection": ChangeLines("discovery/packages.jsonl.gz", rows => rows[0]!["selected"] = new JsonArray(0, 0)); break;
            default: throw new ArgumentOutOfRangeException(nameof(mutation));
        }
        var tree = remoteGit.Run("ls-tree", "-r", "refs/heads/data");
        var inputs = HashFiles(preview);

        AssertRejected();

        Assert.Equal(tree, remoteGit.Run("ls-tree", "-r", "refs/heads/data"));
        Assert.Equal(inputs, HashFiles(preview));
    }

    [Fact]
    public void ReferencedActorCanRemainHeaderOnlyWhileDataAssetIsDecoded()
    {
        var target = AddHeaderSibling("Actor", ["Object"]);
        AddTarget(target);

        Assert.True(Publisher.Publish(preview, remote, NextExtractor, "456").Changed);
    }

    [Theory]
    [InlineData("DataAsset", "Object", "Required export was not selected")]
    [InlineData("UIMetaDataItem", "Object", "Required export was not selected")]
    [InlineData("Texture2D", "Texture", "Referenced export was not decoded")]
    [InlineData("Material", "MaterialInterface", "Referenced export was not decoded")]
    public void RelevantSiblingCannotBeHiddenInHeaderOnlyInventory(string type, string parent, string expected)
    {
        var target = AddHeaderSibling(type, parent == "Object" ? [parent] : [parent, "Object"]);
        if (type is "Texture2D" or "Material") AddTarget(target);

        var error = Assert.Throws<InvalidDataException>(() => Publisher.Publish(preview, remote, NextExtractor, "456"));

        Assert.Contains(expected, error.Message);
        Assert.Equal(initialCommit, RemoteRef("refs/heads/data"));
    }

    [Fact]
    public void RegistryUiTextureCannotRemainHeaderOnlyWithoutAReference()
    {
        var target = AddHeaderSibling("Texture2D", ["Texture", "Object"]);
        ChangeLines("discovery/registry.jsonl.gz", rows => rows.Add(new JsonObject
        {
            ["path"] = target,
            ["package"] = "/Game/DA_Test",
            ["class"] = "Texture2D",
            ["tags"] = new JsonObject { ["LODGroup"] = "TEXTUREGROUP_UI" }
        }));
        ChangeJson("coverage.json", node => node["registeredAssets"] = 3);

        var error = Assert.Throws<InvalidDataException>(() => Publisher.Publish(preview, remote, NextExtractor, "456"));

        Assert.Contains("Required export was not selected", error.Message);
    }

    [Fact]
    public void EmptyPackageDoesNotHideAMissingNamedReference()
    {
        AddUnindexedPackage("PioneerGame/Content/Unknown.uasset", loaded: true);
        AddTarget("/Game/Unknown.Missing");

        var error = Assert.Throws<InvalidDataException>(() => Publisher.Publish(preview, remote, NextExtractor, "456"));

        Assert.Contains("Named reference target is absent", error.Message);
    }

    [Theory]
    [InlineData("/RuntimeOnly/Alias", true)]
    [InlineData("/RuntimeOnly/Alias.DA_Test", false)]
    public void MountedAliasProvesPackageInspectionButNeverRewritesObjectPaths(string target, bool accepted)
    {
        ChangeLines("discovery/registry.jsonl.gz", rows => rows.Add(new JsonObject
        {
            ["path"] = "/RuntimeOnly/Alias.DA_Test",
            ["package"] = "/RuntimeOnly/Alias",
            ["class"] = "Actor",
            ["tags"] = new JsonObject()
        }));
        ChangeJson("coverage.json", node => node["registeredAssets"] = 3);
        ChangeLines("discovery/files.jsonl.gz", rows => rows[0]!["registryPackages"]!.AsArray().Add("/RuntimeOnly/Alias"));
        AddTarget(target);

        if (accepted) Assert.True(Publisher.Publish(preview, remote, NextExtractor, "456").Changed);
        else
        {
            var error = Assert.Throws<InvalidDataException>(() => Publisher.Publish(preview, remote, NextExtractor, "456"));
            Assert.Contains("Named reference target is absent", error.Message);
        }
    }

    [Fact]
    public void UnownedRegistryUiTextureCannotDisappearFromResources()
    {
        ChangeLines("discovery/registry.jsonl.gz", rows => rows[1]!["tags"]!["LODGroup"] = "TEXTUREGROUP_UI");
        ChangeJson("assets.json", rows => rows[0]!["images"] = new JsonArray());
        ChangeJson("coverage.json", node => { node["images"] = 0; node["discovery"]!["resources"] = 0; });
        ChangeJson("resources.json", rows => rows.AsArray().Clear());
        File.Delete(Path.Combine(preview, Image));

        var error = Assert.Throws<InvalidDataException>(() => Publisher.Publish(preview, remote, NextExtractor, "456"));

        Assert.Contains("registry UI texture lacks a published resource", error.Message);
    }

    [Fact]
    public void RegisteredPackageCannotBeOmittedFromHeaderInspection()
    {
        ChangeLines("discovery/files.jsonl.gz", rows => rows.Add(new JsonObject
        {
            ["path"] = "PioneerGame/Content/Map.umap",
            ["registryPackages"] = new JsonArray("/Game/Map")
        }));
        ChangeLines("discovery/registry.jsonl.gz", rows => rows.Add(new JsonObject
        {
            ["path"] = "/Game/Map.Map",
            ["package"] = "/Game/Map",
            ["class"] = "World",
            ["tags"] = new JsonObject()
        }));
        ChangeJson("coverage.json", node => node["registeredAssets"] = 3);

        var error = Assert.Throws<InvalidDataException>(() => Publisher.Publish(preview, remote, NextExtractor, "456"));

        Assert.Contains("mounted package was not inspected", error.Message);
    }

    [Fact]
    public void CanonicalPackageNameCannotContradictTheMountCrosswalk()
    {
        ChangeLines("discovery/packages.jsonl.gz", rows => rows[0]!["name"] = "/Game/T_Test");

        var error = Assert.Throws<InvalidDataException>(() => Publisher.Publish(preview, remote, NextExtractor, "456"));

        Assert.Contains("contradicts its mounted registry owner", error.Message);
    }

    private string AddHeaderSibling(string type, string[] ancestors, string path = "/Game/DA_Test.Sibling")
    {
        ChangeLines("discovery/exports.jsonl.gz", rows => rows.Add(ExportHeader("PioneerGame/Content/DA_Test.uasset", 1, path, type, ancestors)));
        ChangeLines("discovery/packages.jsonl.gz", rows => rows[0]!["exports"] = 2);
        return path;
    }

    private void AddTarget(string path) => ChangeLines("discovery/objects.jsonl.gz", rows =>
    {
        rows[0]!["properties"]!.AsArray().Add(PropertyHeader("/Properties/4", "RelatedAsset", "SoftObjectProperty"));
        rows[0]!["references"]!.AsArray().Add(new JsonObject
        {
            ["pointer"] = "/Properties/4",
            ["kind"] = "soft",
            ["role"] = "property",
            ["targetPath"] = path,
            ["isNull"] = false,
            ["package"] = null,
            ["packageIndex"] = null,
            ["exportIndex"] = null,
            ["error"] = null
        });
    });
}
