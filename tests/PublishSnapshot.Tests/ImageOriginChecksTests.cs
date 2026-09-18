using System.Text.Json;
using System.Text.Json.Nodes;
using CUE4Parse.MappingsProvider;
using static PublishSnapshot.Tests.IdentityFixture;

namespace PublishSnapshot.Tests;

public sealed class ImageOriginChecksTests
{
    private const string Source = "/Game/Item.Item";
    private const string Template = "/Game/Base.Base";
    private const string Texture = "/Game/Icon.Icon";

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ExactHardAndSoftReferencesAreAccepted(bool soft)
    {
        using var fixture = new IdentityFixture();
        Reference(fixture.Object(Source, "PersistenceDataAsset"), "Icon", Texture, soft);
        Validate(fixture, "Icon", Texture);
        Assert.Throws<InvalidDataException>(() => Validate(fixture, "Icon", "/Game/Other.Other"));
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void ADecodedImageAssociationCannotBeOmittedEvenWhenNullOrInherited(bool isNull, bool inherited)
    {
        using var fixture = new IdentityFixture();
        var source = fixture.Object(Source, "PersistenceDataAsset", inherited ? Template : null);
        var owner = inherited ? fixture.Object(Template, "PersistenceDataAsset") : source;
        Reference(owner, "Icon", isNull ? null : Texture, soft: true);
        Validate(fixture, "Icon", isNull ? null : Texture);

        var error = Assert.Throws<InvalidDataException>(() => ValidateImages(fixture, new JsonArray(), Source));

        Assert.Contains("omits decoded image association", error.Message);
    }

    [Theory]
    [InlineData("Icon")]
    [InlineData("BigIcon")]
    public void FieldsSharingTheSameTextureRemainSeparateAssociations(string omitted)
    {
        using var fixture = new IdentityFixture();
        var source = fixture.Object(Source, "PersistenceDataAsset");
        Reference(source, "Icon", Texture, soft: true);
        Reference(source, "BigIcon", Texture, soft: true);
        var images = new JsonArray(Image(Source, "Icon", Texture), Image(Source, "BigIcon", Texture));
        ValidateImages(fixture, images, Source);
        images.Remove(images.Single(image => image!["field"]!.GetValue<string>() == omitted));

        Assert.Throws<InvalidDataException>(() => ValidateImages(fixture, images, Source));
    }

    [Fact]
    public void MetadataImageCannotBeOmittedBehindAnotherSourcesImage()
    {
        using var fixture = new IdentityFixture();
        Reference(fixture.Object(Source, "PersistenceDataAsset"), "Icon", Texture, soft: true);
        Reference(fixture.Object(Template, "UIMetaDataItem"), "Icon", Texture, soft: true);
        var images = new JsonArray(Image(Source, "Icon", Texture), Image(Template, "Icon", Texture));
        ValidateImages(fixture, images, Source, Template);
        images.RemoveAt(1);

        Assert.Throws<InvalidDataException>(() => ValidateImages(fixture, images, Source, Template));
    }

    [Theory]
    [InlineData("PersistenceDataAsset", "Icon")]
    [InlineData("UIEnvironmentalDamageSourceMetaDataItem", "CoverImage")]
    public void MissingSerializedImageFieldsRequireNoAssociation(string type, string field)
    {
        using var fixture = new IdentityFixture();
        if (type != "PersistenceDataAsset") fixture.AddClass(type, "UIMetaDataItem", (field, "SoftObjectProperty"));
        fixture.Object(Source, type);

        ValidateImages(fixture, new JsonArray(), Source);
    }

    [Fact]
    public void SameNamedFieldOnUnrelatedOwnerDoesNotCreateATypedImageAssociation()
    {
        using var fixture = new IdentityFixture();
        Reference(fixture.Object(Source, "PersistenceDataAsset"), "CoverImage", Texture, soft: true);

        ValidateImages(fixture, new JsonArray(), Source);
    }

    [Fact]
    public void ExplicitNullImageMasksTemplateTexture()
    {
        using var fixture = new IdentityFixture();
        Reference(fixture.Object(Source, "PersistenceDataAsset", Template), "Icon", null, soft: true);
        Reference(fixture.Object(Template, "PersistenceDataAsset"), "Icon", Texture, soft: true);
        Validate(fixture, "Icon", null);
        Assert.Throws<InvalidDataException>(() => Validate(fixture, "Icon", Texture));
    }

    [Fact]
    public void InheritedImageUsesActualTemplateAndCaseInsensitiveFieldName()
    {
        using var fixture = new IdentityFixture();
        Reference(fixture.Object(Template, "PersistenceDataAsset"), "Icon", Texture, soft: true);
        fixture.Object(Source, "PersistenceDataAsset", Template);
        Validate(fixture, "icon", Texture.ToUpperInvariant());
    }

    [Theory]
    [InlineData("duplicate")]
    [InlineData("wrong-type")]
    [InlineData("wrong-kind")]
    [InlineData("wrong-role")]
    [InlineData("static-array")]
    [InlineData("conflicting-value")]
    [InlineData("conflicting-text")]
    [InlineData("missing-reference")]
    [InlineData("duplicate-reference")]
    [InlineData("decode-error")]
    [InlineData("null-conflict")]
    public void AmbiguousOrMalformedSourceCannotProveAnImage(string mutation)
    {
        using var fixture = new IdentityFixture();
        var source = fixture.Object(Source, "PersistenceDataAsset");
        Reference(source, "Icon", Texture, soft: true);
        var header = source["properties"]![0]!;
        var link = source["references"]![2]!;
        switch (mutation)
        {
            case "duplicate": Reference(source, "icon", Texture, soft: true); break;
            case "wrong-type": header["type"] = "NameProperty"; break;
            case "wrong-kind": link["kind"] = "hard"; break;
            case "wrong-role": link["role"] = "template"; break;
            case "static-array": header["arraySize"] = 2; break;
            case "conflicting-value": Value(source, "/Properties/0", "NameProperty", "name", "Other"); break;
            case "conflicting-text": source["texts"]!.AsArray().Add(new JsonObject { ["pointer"] = "/Properties/0" }); break;
            case "missing-reference": source["references"]!.AsArray().RemoveAt(2); break;
            case "duplicate-reference": source["references"]!.AsArray().Add(link.DeepClone()); break;
            case "decode-error": link["error"] = "Failed"; break;
            case "null-conflict": link["isNull"] = true; break;
        }
        Assert.Throws<InvalidDataException>(() => Validate(fixture, "Icon", Texture));
    }

    [Theory]
    [InlineData("Missing")]
    [InlineData("Icon[0]")]
    [InlineData("OtherImage")]
    public void UnselectedOrInventedFieldNamesAreRejected(string field)
    {
        using var fixture = new IdentityFixture();
        Reference(fixture.Object(Source, "PersistenceDataAsset"), field, Texture, soft: true);
        Assert.Throws<InvalidDataException>(() => Validate(fixture, field, Texture));
    }

    [Theory]
    [InlineData("UIMapAreaInfoMetaDataItem", "MapAreas", "ArrayProperty", "HeaderImage", "MapAreas[0].HeaderImage", "/0")]
    [InlineData("UIMapWidgetMetaDataItem", "MapWidgetSettings", "StructProperty", "MapTexture", "MapWidgetSettings.MapTexture", "")]
    public void NestedImageReferencesResolveExactOrdinalContainers(string type, string root, string kind,
        string leaf, string field, string index)
    {
        using var fixture = new IdentityFixture();
        fixture.AddClass(type, "UIMetaDataItem", (root, kind));
        var structure = type == "UIMapAreaInfoMetaDataItem" ? "MapAreaInfo" : "MapWidgetLevelSettings";
        fixture.AddClass(structure, null, (leaf, "SoftObjectProperty"));
        var mapping = new PropertyType("StructProperty", structure);
        fixture.Mappings.Types[type].Properties[0].MappingType = kind == "ArrayProperty"
            ? new PropertyType(kind, innerType: mapping) : mapping;
        var source = fixture.Object(Source, type);
        var parent = Header(source, root, kind);
        var pointer = Header(source, leaf, "SoftObjectProperty", parent + index + "/Properties");
        source["references"]!.AsArray().Add(Link(pointer, Texture, "property", "soft"));
        Validate(fixture, field, Texture);
        Assert.Throws<InvalidDataException>(() => ValidateImages(fixture, new JsonArray(), Source));
        Assert.Throws<InvalidDataException>(() => Validate(fixture, field, "/Game/Other.Other"));
        if (index.Length > 0)
        {
            Assert.Throws<InvalidDataException>(() => Validate(fixture, "MapAreas[1].HeaderImage", Texture));
            Assert.Throws<InvalidDataException>(() => Validate(fixture, "MapAreas[00].HeaderImage", Texture));
        }
    }

    [Theory]
    [InlineData("missing-root")]
    [InlineData("empty-array")]
    [InlineData("missing-leaf")]
    public void NestedImageContainerWithoutAnImageLeafRequiresNoAssociation(string shape)
    {
        using var fixture = new IdentityFixture();
        fixture.AddClass("UIMapAreaInfoMetaDataItem", "UIMetaDataItem", ("MapAreas", "ArrayProperty"));
        fixture.AddClass("MapAreaInfo", null, ("HeaderImage", "SoftObjectProperty"));
        fixture.Mappings.Types["UIMapAreaInfoMetaDataItem"].Properties[0].MappingType =
            new("ArrayProperty", innerType: new("StructProperty", "MapAreaInfo"));
        var source = fixture.Object(Source, "UIMapAreaInfoMetaDataItem");
        if (shape != "missing-root")
        {
            var root = Header(source, "MapAreas", "ArrayProperty");
            if (shape == "empty-array") Value(source, root, "ArrayProperty", "empty-array", null);
            else Value(source, root + "/0", "StructProperty", "empty-struct", null);
        }

        ValidateImages(fixture, new JsonArray(), Source);
    }

    [Fact]
    public void EveryNestedArrayLeafRetainsItsOwnOrdinalAndExplicitNull()
    {
        using var fixture = new IdentityFixture();
        fixture.AddClass("UIMapAreaInfoMetaDataItem", "UIMetaDataItem", ("MapAreas", "ArrayProperty"));
        fixture.AddClass("MapAreaInfo", null, ("HeaderImage", "SoftObjectProperty"));
        fixture.Mappings.Types["UIMapAreaInfoMetaDataItem"].Properties[0].MappingType =
            new("ArrayProperty", innerType: new("StructProperty", "MapAreaInfo"));
        var source = fixture.Object(Source, "UIMapAreaInfoMetaDataItem");
        var root = Header(source, "MapAreas", "ArrayProperty");
        var first = Header(source, "HeaderImage", "SoftObjectProperty", root + "/0/Properties");
        var second = Header(source, "HeaderImage", "SoftObjectProperty", root + "/1/Properties");
        source["references"]!.AsArray().Add(Link(first, Texture, "property", "soft"));
        source["references"]!.AsArray().Add(Link(second, null, "property", "soft"));
        var images = new JsonArray(Image(Source, "MapAreas[0].HeaderImage", Texture), Image(Source, "MapAreas[1].HeaderImage", null));
        ValidateImages(fixture, images, Source);
        images.RemoveAt(1);

        Assert.Throws<InvalidDataException>(() => ValidateImages(fixture, images, Source));
    }

    [Fact]
    public void ClassSpecificImageFieldCannotBeBorrowedByAnUnrelatedOwner()
    {
        using var fixture = new IdentityFixture();
        Reference(fixture.Object(Source, "PersistenceDataAsset"), "CoverImage", Texture, soft: true);
        Assert.Throws<InvalidDataException>(() => Validate(fixture, "CoverImage", Texture));
    }

    [Theory]
    [InlineData("wrong-structure")]
    [InlineData("wrong-inner-type")]
    [InlineData("wrong-leaf-type")]
    [InlineData("missing-leaf")]
    public void NestedImageRequiresTheMappedStructAndLeaf(string mutation)
    {
        using var fixture = new IdentityFixture();
        fixture.AddClass("UIMapAreaInfoMetaDataItem", "UIMetaDataItem", ("MapAreas", "ArrayProperty"));
        fixture.AddClass("MapAreaInfo", null, ("HeaderImage", "SoftObjectProperty"));
        var inner = new PropertyType("StructProperty", "MapAreaInfo");
        fixture.Mappings.Types["UIMapAreaInfoMetaDataItem"].Properties[0].MappingType = new("ArrayProperty", innerType: inner);
        var source = fixture.Object(Source, "UIMapAreaInfoMetaDataItem");
        var root = Header(source, "MapAreas", "ArrayProperty");
        var leaf = Header(source, "HeaderImage", "SoftObjectProperty", root + "/0/Properties");
        source["references"]!.AsArray().Add(Link(leaf, Texture, "property", "soft"));
        Validate(fixture, "MapAreas[0].HeaderImage", Texture);
        switch (mutation)
        {
            case "wrong-structure": inner.StructType = "OtherStruct"; break;
            case "wrong-inner-type": inner.Type = "ObjectProperty"; break;
            case "wrong-leaf-type": fixture.Mappings.Types["MapAreaInfo"].Properties[0].MappingType.Type = "ObjectProperty"; break;
            case "missing-leaf": fixture.Mappings.Types["MapAreaInfo"].Properties.Clear(); break;
        }
        Assert.Throws<InvalidDataException>(() => Validate(fixture, "MapAreas[0].HeaderImage", Texture));
    }

    [Fact]
    public void TypedImageRequiresTheDecodedSoftReference()
    {
        using var fixture = new IdentityFixture();
        fixture.AddClass("UIEnvironmentalDamageSourceMetaDataItem", "UIMetaDataItem", ("CoverImage", "SoftObjectProperty"));
        Reference(fixture.Object(Source, "UIEnvironmentalDamageSourceMetaDataItem"), "CoverImage", Texture);
        Assert.Throws<InvalidDataException>(() => Validate(fixture, "CoverImage", Texture));
    }

    private static void Validate(IdentityFixture fixture, string field, string? resource)
        => ValidateImages(fixture, new JsonArray(Image(Source, field, resource)), Source);

    private static JsonObject Image(string source, string field, string? resource) => new()
    {
        ["source"] = source,
        ["field"] = field,
        ["resource"] = resource,
        ["status"] = resource is null ? "absent" : "exported"
    };

    private static void ValidateImages(IdentityFixture fixture, JsonArray images, params string[] sources)
    {
        var (_, _, identities) = fixture.Read(ImageOriginChecks.RootFields.ToArray());
        JsonArray Sources(bool metadata) => new(sources.Where(path => identities.Schema(path).IsA("UIMetaDataItem") == metadata)
            .Select(path => (JsonNode)new JsonObject { ["path"] = path }).ToArray());
        using var rows = JsonDocument.Parse(new JsonArray(new JsonObject
        {
            ["definitions"] = Sources(false),
            ["metadata"] = Sources(true),
            ["images"] = images.DeepClone()
        }).ToJsonString());
        ImageOriginChecks.Validate(rows.RootElement, identities);
    }
}
