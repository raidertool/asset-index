using System.Text.Json.Nodes;
using static PublishSnapshot.Tests.IdentityFixture;

namespace PublishSnapshot.Tests;

public sealed class IdentityContextTests
{
    private const string Item = "/Game/Item.Item";
    private const string Persistence = "/Game/Identity.Identity";
    private const string Metadata = "/Game/Ui.Ui";
    private const string Template = "/Game/Template.Template";

    [Theory]
    [InlineData("42")]
    [InlineData("-9223372036854775808")]
    [InlineData("9223372036854775807")]
    public void DirectIdentityIsReadFromTypedPersistenceField(string id)
    {
        using var fixture = new IdentityFixture();
        var persistence = fixture.Object(Persistence, "PersistenceDataAsset");
        Number(persistence, "AssetId", id);
        var context = fixture.Read().Context;
        Validate(context, Catalog(id, [persistence]));
        Assert.Throws<InvalidDataException>(() => Validate(context, Catalog("999999", [persistence])));
    }

    [Theory]
    [InlineData("0")]
    [InlineData("042")]
    [InlineData("+42")]
    [InlineData(" 42")]
    [InlineData("9223372036854775808")]
    public void IdentityMustBeCanonicalNonzeroInt64(string id)
    {
        using var fixture = new IdentityFixture();
        var source = fixture.Object(Persistence, "PersistenceDataAsset");
        Number(source, "AssetId", id);
        Assert.Throws<InvalidDataException>(() => Validate(fixture.Read().Context, Catalog(id, [source])));
    }

    [Fact]
    public void DefinitionAndMetadataMustResolveTheSameIncludedIdentity()
    {
        using var fixture = new IdentityFixture();
        var persistence = fixture.Object(Persistence, "PersistenceDataAsset");
        Number(persistence, "AssetId", "42");
        var item = fixture.Object(Item, "ItemDataAssetBase");
        Reference(item, "PersistenceDataAsset", Persistence);
        var metadata = fixture.Object(Metadata, "UIMetaDataItem");
        Reference(metadata, "PersistenceDataAsset", Persistence);
        var context = fixture.Read().Context;
        Validate(context, Catalog("42", [item, persistence], [metadata]));
        Assert.Throws<InvalidDataException>(() => Validate(context, Catalog("42", [item], [metadata])));
        Assert.Throws<InvalidDataException>(() => Validate(context, Catalog("43", [item, persistence], [metadata])));
        Assert.Throws<InvalidDataException>(() => Validate(context, Catalog("42", [persistence], [item])));
    }

    [Fact]
    public void SoftIdentityReferenceMustAgreeWithItsMappingAndRecordedKind()
    {
        using var fixture = new IdentityFixture();
        fixture.Mappings.Types["ItemDataAssetBase"].Properties.Values.Single(property => property.Name == "PersistenceDataAsset")
            .MappingType = new("SoftObjectProperty");
        var persistence = fixture.Object(Persistence, "PersistenceDataAsset");
        Number(persistence, "AssetId", "42");
        var item = fixture.Object(Item, "ItemDataAssetBase");
        Reference(item, "PersistenceDataAsset", Persistence, soft: true);
        Validate(fixture.Read().Context, Catalog("42", [item, persistence]));
        item["references"]![2]!["kind"] = "hard";
        Assert.Throws<InvalidDataException>(() => Validate(fixture.Read().Context, Catalog("42", [item, persistence])));
    }

    [Fact]
    public void EnabledOverrideUsesItsOwnIdentityWithoutBorrowingPersistenceIdentity()
    {
        using var fixture = new IdentityFixture();
        var persistence = fixture.Object(Persistence, "PersistenceDataAsset");
        Number(persistence, "AssetId", "42");
        var item = fixture.Object(Item, "ItemDataAssetBase");
        Boolean(item, "bOverrideItemAssetId", true);
        Number(item, "OverrideItemAssetId", "43");
        Reference(item, "PersistenceDataAsset", Persistence);
        var context = fixture.Read().Context;
        Validate(context, Catalog("43", [item]), Catalog("42", [persistence]));
        Assert.Throws<InvalidDataException>(() => Validate(context, Catalog("42", [item, persistence])));
        Assert.Throws<InvalidDataException>(() => Validate(context, Catalog("43", [item, persistence])));
    }

    [Fact]
    public void DisabledOverrideDoesNotHidePersistenceIdentity()
    {
        using var fixture = new IdentityFixture();
        var persistence = fixture.Object(Persistence, "PersistenceDataAsset");
        Number(persistence, "AssetId", "42");
        var item = fixture.Object(Item, "ItemDataAssetBase");
        Boolean(item, "bOverrideItemAssetId", false);
        Number(item, "OverrideItemAssetId", "43");
        Reference(item, "PersistenceDataAsset", Persistence);
        Validate(fixture.Read().Context, Catalog("42", [item, persistence]));
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("zero")]
    [InlineData("invalid-flag")]
    public void EnabledOverrideRequiresTypedNonzeroValue(string mutation)
    {
        using var fixture = new IdentityFixture();
        var item = fixture.Object(Item, "ItemDataAssetBase");
        Boolean(item, "bOverrideItemAssetId", true);
        if (mutation != "missing") Number(item, "OverrideItemAssetId", mutation == "zero" ? "0" : "42");
        if (mutation == "invalid-flag") item["values"]![0]!["value"] = "1";
        Assert.Throws<InvalidDataException>(() => Validate(fixture.Read().Context, Catalog("42", [item])));
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("cycle")]
    [InlineData("wrong-kind")]
    [InlineData("conflicting-value")]
    public void UnreadableTemplateCannotSupplyDefaultOverrideSemantics(string mutation)
    {
        using var fixture = new IdentityFixture();
        var item = fixture.Object(Item, "ItemDataAssetBase", Template);
        var template = fixture.Object(Template, "ItemDataAssetBase");
        Boolean(template, "bOverrideItemAssetId", true);
        Number(template, "OverrideItemAssetId", "42");
        switch (mutation)
        {
            case "missing": fixture.Objects.Remove(template); break;
            case "cycle": item["references"]![1]!["targetPath"] = Item; break;
            case "wrong-kind": item["references"]![1]!["kind"] = "hard"; break;
            case "conflicting-value": Value(item, "/Template", "ObjectProperty", "null", null); break;
        }
        Assert.Throws<InvalidDataException>(() => Validate(fixture.Read().Context, Catalog("42", [item])));
    }

    [Theory]
    [InlineData("UIPlayerStatsRaiderTargetMetaDataItem", "PlayerStatsRaiderTargetDataAsset")]
    [InlineData("UIInteractQuestMetaDataItem", "InteractQuestDataAsset")]
    [InlineData("UIQuestAreaMetaDataItem", "WorldQuestDataAsset")]
    [InlineData("UIXPEventCategoryMetaDataItem", "XPEventCategoryDataAsset")]
    [InlineData("UIQuestObjectiveParameterMetaDataItem", "Asset")]
    public void MetadataUsesItsDeclaredNativeIdentityLink(string type, string field)
    {
        using var fixture = new IdentityFixture();
        fixture.AddClass(type, "UIMetaDataItem", (field, "ObjectProperty"));
        var persistence = fixture.Object(Persistence, "PersistenceDataAsset");
        Number(persistence, "AssetId", "42");
        var metadata = fixture.Object(Metadata, type);
        Reference(metadata, field, Persistence);
        Validate(fixture.Read().Context, Catalog("42", [persistence], [metadata]));
    }

    [Fact]
    public void MetadataOverrideDoesNotNeedItsUnusedReference()
    {
        using var fixture = new IdentityFixture();
        var persistence = fixture.Object(Persistence, "PersistenceDataAsset");
        Number(persistence, "AssetId", "42");
        var metadata = fixture.Object(Metadata, "UIMetaDataItem");
        Boolean(metadata, "bOverrideAssetId", true);
        Number(metadata, "OverrideAssetId", "42");
        Validate(fixture.Read().Context, Catalog("42", [persistence], [metadata]));
    }

    [Fact]
    public void TemplateIdentityIsAllowedOnlyWithinQualifiedClassAncestry()
    {
        using var fixture = new IdentityFixture();
        var source = fixture.Object(Persistence, "PersistenceDataAsset", Template);
        var template = fixture.Object(Template, "PersistenceDataAsset");
        Number(template, "AssetId", "42");
        Validate(fixture.Read().Context, Catalog("42", [source]));
        fixture.AddClass("Unrelated", "DataAsset", ("AssetId", "Int64Property"));
        template["class"] = "Unrelated";
        template["references"]![0]!["targetPath"] = "/Script/Test.Unrelated";
        fixture.Exports[1]["classPath"] = "/Script/Test.Unrelated";
        Assert.Throws<InvalidDataException>(() => Validate(fixture.Read().Context, Catalog("42", [source])));
    }

    [Theory]
    [InlineData("header-type")]
    [InlineData("scalar-type")]
    [InlineData("kind")]
    [InlineData("array")]
    [InlineData("binary")]
    [InlineData("duplicate")]
    [InlineData("diagnostic")]
    [InlineData("missing-class-mapping")]
    [InlineData("class-header")]
    [InlineData("native-role-spoof")]
    public void MalformedOrUntrustedEvidenceCannotSupplyAnIdentity(string mutation)
    {
        using var fixture = new IdentityFixture();
        var source = fixture.Object(Persistence, "PersistenceDataAsset");
        Number(source, "AssetId", "42");
        switch (mutation)
        {
            case "header-type": source["properties"]![0]!["type"] = "IntProperty"; break;
            case "scalar-type": source["values"]![0]!["type"] = "IntProperty"; break;
            case "kind": source["values"]![0]!["kind"] = "string"; break;
            case "array": source["properties"]![0]!["arraySize"] = 2; break;
            case "binary": source["properties"]![0]!["serializeType"] = "BinaryOrNative"; break;
            case "duplicate": Number(source, "AssetId", "42"); break;
            case "diagnostic": source["issues"]!.AsArray().Add(new JsonObject { ["pointer"] = "/Class" }); break;
            case "missing-class-mapping": fixture.Mappings.Types.Remove("PersistenceDataAsset"); break;
            case "class-header": fixture.Exports[0]["classPath"] = "/Script/Test.Other"; break;
            case "native-role-spoof":
                fixture.Mappings.Types["PersistenceDataAsset"].Name = "Other";
                fixture.Mappings.Types.Remove("PersistenceDataAsset");
                fixture.AddClass("Other", "DataAsset", ("AssetId", "Int64Property"));
                source["class"] = "Other";
                source["references"]![0]!["targetPath"] = "/Script/Test.Other";
                fixture.Exports[0]["classPath"] = "/Script/Test.Other";
                break;
        }
        Assert.Throws<InvalidDataException>(() => Validate(fixture.Read().Context, Catalog("42", [source])));
    }

    [Fact]
    public void OptionalLocalDefinitionHasNoCatalogIdentity()
    {
        using var fixture = new IdentityFixture();
        var item = fixture.Object(Item, "ItemDataAssetBase");
        Reference(item, "PersistenceDataAsset", null);
        var context = fixture.Read().Context;
        Assert.Null(context.DefinitionId(Item));
        Assert.Throws<InvalidDataException>(() => Validate(context, Catalog("42", [item])));
    }

    [Fact]
    public void DuplicateInheritedIdentityDeclarationsAreRejected()
    {
        using var fixture = new IdentityFixture();
        fixture.AddClass("BadPersistence", "PersistenceDataAsset", ("AssetId", "Int64Property"));
        var source = fixture.Object(Persistence, "BadPersistence");
        Number(source, "AssetId", "42");
        Assert.Throws<InvalidDataException>(() => Validate(fixture.Read().Context, Catalog("42", [source])));
    }
}
