using System.Text.Json.Nodes;

namespace PublishSnapshot.Tests;

public sealed partial class PublisherTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ProvenNonCatalogNativeHeaderDoesNotRequireAFieldLayout(bool referenced)
    {
        var target = AddUnmappedNativeHeader();
        if (referenced) AddTarget(target);

        using var accepted = Preview.Read(preview);
        using var snapshot = DataSnapshot.Create(accepted);
        var coverage = JsonNode.Parse(File.ReadAllText(snapshot.Files["coverage.json"].Path))!;
        Assert.Equal(1, coverage["exploration"]!["unmappedNonCatalogExports"]!.GetValue<int>());
    }

    [Theory]
    [InlineData("class-path")]
    [InlineData("ancestry")]
    [InlineData("count")]
    [InlineData("decoded")]
    public void IncompleteHeaderCannotForgeScopeOrClaimDecodedFields(string mutation)
    {
        var target = AddUnmappedNativeHeader();
        switch (mutation)
        {
            case "class-path": ChangeLines("discovery/exports.jsonl.gz", rows => rows[^1]!["classPath"] = "/Script/Other.AISensingStatusTransition"); break;
            case "ancestry": ChangeLines("discovery/exports.jsonl.gz", rows => rows[^1]!["ancestry"]!.AsArray().Add("DataAsset")); break;
            case "count": ChangeJson("coverage.json", row => row["discovery"]!["unmappedNonCatalogExports"] = 0); break;
            case "decoded": AddSiblingBody(target, "AISensingStatusTransition"); break;
        }

        AssertRejected();
    }

    [Theory]
    [InlineData("identity")]
    [InlineData("template")]
    public void OmittedBodyCannotSupplyAnIdentityOrInheritedCatalogValue(string kind)
    {
        var target = AddUnmappedNativeHeader();
        if (kind == "identity")
        {
            ChangeLines("discovery/objects.jsonl.gz", rows => rows[0]!["references"]!.AsArray()
                .Single(link => link!["pointer"]!.GetValue<string>() == "/Properties/0")!["targetPath"] = target);
        }
        else
        {
            const string identity = "/Game/DA_Identity.DA_Identity";
            ChangeLines("discovery/objects.jsonl.gz", rows =>
            {
                var source = rows.Single(row => row!["path"]!.GetValue<string>() == identity)!;
                source["properties"] = new JsonArray();
                source["values"] = new JsonArray();
            });
            AddRequiredReference(target, "template", "/Template", identity);
        }

        AssertRejected();
    }

    [Fact]
    public void UnrelatedClassDefaultCanRemainAnUnmappedHeader()
    {
        var target = AddUnmappedNativeHeader();
        var declaration = AddHeaderSibling("BlueprintGeneratedClass", ["Class", "Struct", "Field", "Object"],
            "/Game/DA_Test.UnrelatedClass", index: 2);
        AddSiblingBody(declaration, "BlueprintGeneratedClass", index: 2);
        AddRequiredReference(target, "class-default", "/Native/ClassDefaultObject", declaration);

        using var accepted = Preview.Read(preview);
    }

    [Theory]
    [InlineData("valid")]
    [InlineData("body-class")]
    [InlineData("body-parent")]
    [InlineData("header-parent")]
    [InlineData("ancestry")]
    [InlineData("parent-kind")]
    [InlineData("parent-value")]
    [InlineData("parent-child-value")]
    public void WidgetScopeRequiresTheExactDeclarationAndMatchingBody(string mutation)
    {
        const string declaration = "/Game/DA_Test.DebugWidget_C";
        const string meta = "/Script/UMG.WidgetBlueprintGeneratedClass";
        const string parent = "/Script/Fixture.UnmappedDebugWidget";
        AddHeaderSibling("WidgetBlueprintGeneratedClass", ["BlueprintGeneratedClass", "Class", "Struct", "Field", "Object"], declaration);
        AddSiblingBody(declaration, "WidgetBlueprintGeneratedClass");
        ChangeLines("discovery/exports.jsonl.gz", rows =>
        {
            rows[^1]!["classPath"] = meta;
            rows[^1]!["superPath"] = parent;
        });
        ChangeLines("discovery/objects.jsonl.gz", rows =>
        {
            var references = rows[^1]!["references"]!.AsArray();
            references.Single(link => link!["pointer"]!.GetValue<string>() == "/Class")!["targetPath"] = meta;
        });
        AddRequiredReference(parent, "super", "/Native/SuperStruct", declaration);
        ChangeLines("discovery/objects.jsonl.gz", rows => rows[^1]!["references"]!.AsArray()
            .Single(link => link!["pointer"]!.GetValue<string>() == "/Native/SuperStruct")!["kind"] = "hard");
        var target = AddHeaderSibling("DebugWidget_C", ["UnmappedDebugWidget"], "/Game/DA_Test.Widget", index: 2);
        ChangeLines("discovery/exports.jsonl.gz", rows =>
        {
            rows[^1]!["classPath"] = declaration;
            rows[^1]!["ancestryComplete"] = false;
            rows[^1]!["error"] = "Field layout unavailable";
        });
        AddUnmappedCount();
        switch (mutation)
        {
            case "body-class": ChangeLines("discovery/objects.jsonl.gz", rows => rows[^1]!["references"]![0]!["targetPath"] = "/Script/Fixture.WidgetBlueprintGeneratedClass"); break;
            case "body-parent": AddRequiredReference("/Script/Fixture.Other", "super", "/Native/SuperStruct", declaration); break;
            case "header-parent": ChangeLines("discovery/exports.jsonl.gz", rows => rows.Single(row => row!["path"]!.GetValue<string>() == declaration)!["superPath"] = "/Script/Fixture.Other"); break;
            case "ancestry": ChangeLines("discovery/exports.jsonl.gz", rows => rows[^1]!["ancestry"] = new JsonArray("DebugWidget_C", "DataAsset")); break;
            case "parent-kind":
                ChangeLines("discovery/objects.jsonl.gz", rows => rows[^1]!["references"]!.AsArray()
                .Single(link => link!["pointer"]!.GetValue<string>() == "/Native/SuperStruct")!["kind"] = "soft"); break;
            case "parent-value":
            case "parent-child-value":
                ChangeLines("discovery/objects.jsonl.gz", rows => rows[^1]!["values"]!.AsArray().Add(new JsonObject
                {
                    ["pointer"] = mutation == "parent-value" ? "/Native/SuperStruct" : "/Native/SuperStruct/Name",
                    ["type"] = "FName",
                    ["kind"] = "name",
                    ["value"] = "Other"
                })); break;
        }
        AddTarget(target);

        if (mutation == "valid") { using var accepted = Preview.Read(preview); }
        else AssertRejected();
    }

    private string AddUnmappedNativeHeader()
    {
        var target = AddHeaderSibling("AISensingStatusTransition", []);
        ChangeLines("discovery/exports.jsonl.gz", rows =>
        {
            rows[^1]!["classPath"] = "/Script/Angelscript.AISensingStatusTransition";
            rows[^1]!["ancestryComplete"] = false;
            rows[^1]!["error"] = "Field layout unavailable";
        });
        AddUnmappedCount();
        return target;
    }

    private void AddUnmappedCount() => ChangeJson("coverage.json", row =>
    {
        row["discovery"]!["unavailableSoftReferences"] = 0;
        row["discovery"]!["unavailableHardReferences"] = 0;
        row["discovery"]!["unmappedNonCatalogExports"] = 1;
    });
}
