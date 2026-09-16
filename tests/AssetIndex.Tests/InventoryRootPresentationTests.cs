using CUE4Parse.MappingsProvider;
using CUE4Parse.MappingsProvider.Usmap;
using CUE4Parse.UE4.Assets;
using CUE4Parse.UE4.Assets.Exports;
using CUE4Parse.UE4.Assets.Objects;
using CUE4Parse.UE4.Assets.Objects.Properties;
using CUE4Parse.UE4.Objects.Core.i18N;
using CUE4Parse.UE4.Objects.GameplayTags;
using CUE4Parse.UE4.Objects.UObject;

namespace AssetIndex.Tests;

public sealed class InventoryRootPresentationTests
{
    private static readonly Lazy<TypeMappings> Mappings = new(() =>
        new FileUsmapTypeMappingsProvider(Path.Combine(AppContext.BaseDirectory, "mappings", "ArcRaiders.usmap")).MappingsForGame!);

    [Theory]
    [InlineData("StashSlot", "Stash")]
    [InlineData("ExpeditionStashSlot", "Stash")]
    [InlineData("BonusStashSlot", "Stash")]
    [InlineData("SecretStashSlot", "Stash")]
    [InlineData("AugmentSlot", "Augment")]
    public void RootFieldNamesDoNotEstablishUiCategoryOwnership(string field, string type)
    {
        var issues = new List<ExtractionIssue>();

        var assets = Assets.Collect(Fixture(field, type), Mappings.Value, issues);

        Assert.Equal(new long[] { 42, 43, 44 }, assets.Select(asset => asset.Id));
        foreach (var asset in assets)
        {
            Assert.Contains(asset.Definitions, source => source.Reference.Class is
                "InventoryContainerSlotDataAsset" or "InventoryContainerItemDataAsset");
            Assert.Empty(asset.PresentationNames);
            var text = Text.Read(asset, issues);
            Assert.Null(text.Name);
            Assert.Empty(text.Candidates);
        }
        Assert.Empty(issues);
    }

    [Theory]
    [InlineData(42)]
    [InlineData(43)]
    public void ExplicitUiMetadataStillNamesRootLinkedObjects(long id)
    {
        var objects = Fixture("StashSlot", "Stash");
        objects.Add(Object("SpecificUi", "UIGameplayItemMetaDataItem",
            ("bOverrideAssetId", new BoolProperty(true)), ("OverrideAssetId", new Int64Property(id)),
            ("ItemName", new TextProperty(new FText("Authored label")))));
        var issues = new List<ExtractionIssue>();

        var asset = Assets.Collect(objects, Mappings.Value, issues).Single(asset => asset.Id == id);

        Assert.Equal("Authored label", Text.Read(asset, issues).Name?.Source);
        Assert.Single(asset.Metadata);
        Assert.Empty(issues);
    }

    private static List<UObject> Fixture(string field, string type)
    {
        var item = Item("OpaqueA", 43, "Container");
        var slot = Object("OpaqueB", "InventoryContainerSlotDataAsset",
            ("PersistenceDataAsset", Reference(Identity(42))), ("DefaultContainer", Reference(item)),
            ("AllowedContainersQuery", Query()));
        var root = Object("OpaqueC", "InventoryTreeRootAsset", (field, Reference(slot)));
        var label = Object("OpaqueD", "UIInventoryContainerMetaDataItem",
            ("ContainerType", new EnumProperty(new FName("ENewInventoryContainerType::" + type))),
            ("ContainerName", new TextProperty(new FText("Categories", "Visible", "Visible category"))));
        return [root, slot, item, Item("OpaqueE", 44, "Container.Child"), label];
    }

    private static UObject Identity(long id) => Object("Identity" + id, "PersistenceDataAsset", ("AssetId", new Int64Property(id)));
    private static UObject Item(string name, long id, string tag) => Object(name, "InventoryContainerItemDataAsset",
        ("PersistenceDataAsset", Reference(Identity(id))),
        ("Tags", new StructProperty(new FScriptStruct(new FGameplayTagContainer([new FGameplayTag(tag)])))));
    private static StructProperty Query() => new(new FScriptStruct(new FStructFallback([
        new FPropertyTag { Name = "TagDictionary", Tag = new ArrayProperty(new UScriptArray([
            new StructProperty(new FScriptStruct(new FGameplayTag("Container")))], "StructProperty")) },
        new FPropertyTag { Name = "QueryTokenStream", Tag = new ArrayProperty(new UScriptArray(
            new byte[] { 0, 1, 1, 1, 0 }.Select(token => (FPropertyTagType)new ByteProperty(token)).ToList(), "ByteProperty")) }
    ])));
    private static ObjectProperty Reference(UObject source) => new(new FPackageIndex(new FixturePackage(source), 1));
    private static UObject Object(string name, string type, params (string Name, FPropertyTagType Value)[] properties) =>
        new(properties.Select(property => new FPropertyTag { Name = property.Name, Tag = property.Value }).ToList())
        { Name = name, Class = new ResolvedLoadedObject(new UScriptClass(type)) };
}
