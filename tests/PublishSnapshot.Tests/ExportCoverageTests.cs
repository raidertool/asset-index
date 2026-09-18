using System.Text.Json;
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
            case "incomplete-ancestry": ChangeLines("discovery/exports.jsonl.gz", rows => rows[0]!["ancestry"] = new JsonArray(rows[0]!["class"]!.GetValue<string>())); break;
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
        var tree = remoteGit.Run("ls-tree", "-r", "refs/heads/main");
        var inputs = HashFiles(preview);

        AssertRejected();

        Assert.Equal(tree, remoteGit.Run("ls-tree", "-r", "refs/heads/main"));
        Assert.Equal(inputs, HashFiles(preview));
    }

    [Theory]
    [InlineData("/Script/CoreUObject.Object", true)]
    [InlineData("/Game/DA_Test.MissingParent", false)]
    [InlineData("/Game/DA_Test", false)]
    [InlineData("UnqualifiedParent", false)]
    public void HeaderSuperclassRequiresAnExactReference(string parent, bool accepted)
    {
        ChangeLines("discovery/exports.jsonl.gz", rows => rows[0]!["superPath"] = parent);

        if (accepted) Assert.True(Publisher.Publish(preview, remote, NextExtractor, "456").Changed);
        else AssertRejected();
    }

    [Fact]
    public void ExportClassNamesUseUnrealCaseInsensitiveIdentity()
    {
        ChangeLines("discovery/exports.jsonl.gz", rows =>
            rows[0]!["ancestry"] = new JsonArray(rows[0]!["ancestry"]!.AsArray()
                .Select(value => (JsonNode?)JsonValue.Create(value!.GetValue<string>().ToLowerInvariant())).ToArray()));
        ChangeLines("discovery/objects.jsonl.gz", rows =>
            rows[0]!["class"] = rows[0]!["class"]!.GetValue<string>().ToUpperInvariant());

        Assert.True(Publisher.Publish(preview, remote, NextExtractor, "456").Changed);
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
    [InlineData("Class", "Struct", "Referenced export was not decoded")]
    [InlineData("UserDefinedStruct", "Struct", "Referenced export was not decoded")]
    [InlineData("TextBlock", "Widget", "Referenced export was not decoded")]
    [InlineData("WidgetTree", "Object", "Referenced export was not decoded")]
    [InlineData("OverlaySlot", "PanelSlot", "Referenced export was not decoded")]
    public void RelevantSiblingCannotBeHiddenInHeaderOnlyInventory(string type, string parent, string expected)
    {
        var target = AddHeaderSibling(type, parent == "Object" ? [parent] : [parent, "Object"]);
        if (expected == "Referenced export was not decoded") AddTarget(target);

        var error = Assert.Throws<InvalidDataException>(() => Publisher.Publish(preview, remote, NextExtractor, "456"));

        Assert.Contains(expected, error.Message);
        Assert.Equal(initialCommit, RemoteRef("refs/heads/main"));
    }

    [Fact]
    public void UnreferencedSchemaCanRemainHeaderOnly()
    {
        AddHeaderSibling("UserDefinedStruct", ["Struct", "Field", "Object"]);

        Assert.True(Publisher.Publish(preview, remote, NextExtractor, "456").Changed);
    }

    [Theory]
    [InlineData("TextBlock", "Widget")]
    [InlineData("WidgetTree", "Object")]
    [InlineData("OverlaySlot", "PanelSlot")]
    public void UnreferencedUiBodiesAreNotNewSeeds(string type, string parent)
    {
        AddHeaderSibling(type, parent == "Object" ? [parent] : [parent, "Object"]);

        using var accepted = Preview.Read(preview);
    }

    [Theory]
    [InlineData("TextBlock", "Widget")]
    [InlineData("WidgetTree", "Object")]
    [InlineData("OverlaySlot", "PanelSlot")]
    public void DecodedUiBodiesSatisfyTheirExactReferences(string type, string parent)
    {
        var target = AddHeaderSibling(type, parent == "Object" ? [parent] : [parent, "Object"]);
        AddTarget(target.ToLowerInvariant());
        AddSiblingBody(target, type);

        using var accepted = Preview.Read(preview);
    }

    [Theory]
    [InlineData("class-default", "/Native/ClassDefaultObject")]
    [InlineData("template", "/Template")]
    public void RequiredBodiesCannotBeRemovedEvenIfOrdinaryReferencesSawTheirHeaders(string role, string pointer)
    {
        var target = AddHeaderSibling("Actor", ["Object"]);
        AddTarget(target);
        AddSiblingBody(target, "Actor");
        var ownerClass = role == "template" ? "Actor" : "BlueprintGeneratedClass";
        var owner = AddHeaderSibling(ownerClass, role == "template" ? ["Object"] : ["Class", "Struct", "Field", "Object"],
            "/Game/DA_Test.ReferenceOwner", index: 2);
        AddSiblingBody(owner, ownerClass, index: 2);
        AddRequiredReference(target.ToLowerInvariant(), role, pointer, owner);
        using (Preview.Read(preview)) { }
        ChangeLines("discovery/objects.jsonl.gz", rows => rows.Remove(rows.Single(row => row!["path"]!.GetValue<string>() == target)));
        ChangeJson("coverage.json", node => node["discovery"]!["objects"] = node["discovery"]!["objects"]!.GetValue<int>() - 1);
        ChangeLines("discovery/packages.jsonl.gz", rows =>
        {
            rows[0]!["selected"] = new JsonArray(0, 2);
            rows[0]!["decoded"] = new JsonArray(0, 2);
        });
        var references = remoteGit.Run("show-ref");
        var inputs = HashFiles(preview);

        var error = Assert.Throws<InvalidDataException>(() => Publisher.Publish(preview, remote, NextExtractor, "456"));

        Assert.Contains("Referenced export was not decoded", error.Message);
        Assert.Equal(references, remoteGit.Run("show-ref"));
        Assert.Equal(inputs, HashFiles(preview));
    }

    [Theory]
    [InlineData("property", "/Native/ClassDefaultObject")]
    [InlineData("property", "/Template")]
    [InlineData("class-default", "/Other")]
    [InlineData("template", "/Other")]
    public void ExactReferenceRolesCannotBeChangedToEvadeBodyRequirements(string role, string pointer)
    {
        var target = AddHeaderSibling("Actor", ["Object"]);
        AddRequiredReference(target, role, pointer);

        AssertRejected();
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
        ChangeJson("coverage.json", node => node["registeredAssets"] = node["registeredAssets"]!.GetValue<int>() + 1);

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
        ChangeJson("coverage.json", node => node["registeredAssets"] = node["registeredAssets"]!.GetValue<int>() + 1);
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
        ChangeJson("coverage.json", node => node["registeredAssets"] = node["registeredAssets"]!.GetValue<int>() + 1);

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

    private string AddHeaderSibling(string type, string[] ancestors, string path = "/Game/DA_Test.Sibling", int index = 1)
    {
        ChangeLines("discovery/exports.jsonl.gz", rows => rows.Add(ExportHeader("PioneerGame/Content/DA_Test.uasset", index, path, type, ancestors)));
        ChangeLines("discovery/packages.jsonl.gz", rows => rows[0]!["exports"] = index + 1);
        return path;
    }

    private void AddSiblingBody(string path, string type, int index = 1)
    {
        ChangeLines("discovery/objects.jsonl.gz", rows => rows.Add(new JsonObject
        {
            ["path"] = path,
            ["class"] = type,
            ["properties"] = new JsonArray(),
            ["references"] = JsonSerializer.SerializeToNode(ObjectLinks(type)),
            ["texts"] = new JsonArray(),
            ["values"] = new JsonArray(),
            ["tableEntries"] = new JsonArray(),
            ["issues"] = new JsonArray()
        }));
        ChangeJson("coverage.json", node => node["discovery"]!["objects"] = node["discovery"]!["objects"]!.GetValue<int>() + 1);
        ChangeLines("discovery/packages.jsonl.gz", rows =>
        {
            rows[0]!["selected"]!.AsArray().Add(index);
            rows[0]!["decoded"]!.AsArray().Add(index);
        });
    }

    private void AddRequiredReference(string path, string role, string pointer, string? owner = null) => ChangeLines("discovery/objects.jsonl.gz", rows =>
    {
        var source = owner is null ? rows[0]! : rows.Single(row => row!["path"]!.GetValue<string>() == owner)!;
        var references = source["references"]!.AsArray();
        var existing = references.SingleOrDefault(reference => reference!["pointer"]!.GetValue<string>() == pointer);
        if (existing is not null) references.Remove(existing);
        references.Add(new JsonObject
        {
            ["pointer"] = pointer,
            ["kind"] = role == "class-default" ? "hard" : "resolved",
            ["role"] = role,
            ["targetPath"] = path,
            ["isNull"] = false,
            ["package"] = null,
            ["packageIndex"] = null,
            ["exportIndex"] = null,
            ["error"] = null
        });
    });

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
