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
    public void RootFieldsLabelTheirSlotDefaultAndEligibleContainers(string field, string type)
    {
        var (objects, root, slot, _) = Fixture(field, type);
        var issues = new List<ExtractionIssue>();

        var assets = Assets.Collect(objects, Mappings.Value, issues);

        foreach (var id in new long[] { 42, 43, 44 })
        {
            var asset = assets.Single(asset => asset.Id == id);
            var name = Assert.Single(asset.InventoryRootNames);
            Assert.Equal("Visible category", Text.Read(asset, issues).Name?.Source);
            Assert.Equal(root.GetPathName(), name.Match.Root.Path);
            Assert.Equal(field, name.Match.Root.Field);
            Assert.Equal(root.GetPathName(), name.Match.Root.DefinedAt);
            Assert.Equal(slot.GetPathName(), name.Match.SlotPath);
            Assert.Equal(slot.GetPathName(), name.Match.Root.Query!.DefinedAt);
            Assert.Equal(id == 42 ? "container-slot" : id == 43 ? "default-container" : "allowed-container", name.Match.Role);
        }
        Assert.Empty(assets.Single(asset => asset.Id == 45).InventoryRootNames);
        Assert.Empty(issues);
    }

    [Fact]
    public void DifferentRootCategoriesPreserveAmbiguityInsteadOfChoosingTheFirst()
    {
        var (objects, root, slot, _) = Fixture();
        Add(root, "AugmentSlot", Reference(slot));
        objects.Add(Label("OtherUi", "Augment", "Different category"));
        var issues = new List<ExtractionIssue>();

        var asset = Assets.Collect(objects, Mappings.Value, issues).Single(asset => asset.Id == 42);

        Assert.Equal(2, asset.InventoryRootNames.Count);
        Assert.Null(Text.Read(asset, issues).Name);
        Assert.Empty(issues);
    }

    [Fact]
    public void ExplicitUiNameWinsWhileRootCategoryRemainsEvidence()
    {
        var (objects, _, _, _) = Fixture();
        objects.Add(Object("SpecificUi", "UIGameplayItemMetaDataItem",
            ("bOverrideAssetId", new BoolProperty(true)), ("OverrideAssetId", new Int64Property(44)),
            ("ItemName", new TextProperty(new FText("Specific item")))));
        var issues = new List<ExtractionIssue>();

        var asset = Assets.Collect(objects, Mappings.Value, issues).Single(asset => asset.Id == 44);

        Assert.Equal("Specific item", Text.Read(asset, issues).Name?.Source);
        Assert.Single(asset.InventoryRootNames);
        Assert.Empty(issues);
    }

    [Fact]
    public void EffectiveIdentityOverrideDoesNotLabelTheOriginalPersistenceId()
    {
        var (objects, _, slot, _) = Fixture();
        Add(slot, "bOverrideItemAssetId", new BoolProperty(true));
        Add(slot, "OverrideItemAssetId", new Int64Property(142));
        var issues = new List<ExtractionIssue>();

        var assets = Assets.Collect(objects, Mappings.Value, issues);

        Assert.Equal("Visible category", Text.Read(assets.Single(asset => asset.Id == 142), issues).Name?.Source);
        Assert.DoesNotContain(assets.SelectMany(asset => asset.InventoryRootNames), name => name.AssetId == 42);
        Assert.Empty(issues);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void TemplateRootFieldIsUsedUntilExplicitlyOverridden(bool clear)
    {
        var (objects, template, _, _) = Fixture();
        template.Flags |= EObjectFlags.RF_ClassDefaultObject;
        var root = Object("InstanceRoot", "InventoryTreeRootAsset");
        root.Template = new ResolvedLoadedObject(template);
        if (clear) Add(root, "StashSlot", new ObjectProperty(new FPackageIndex()));
        objects.Add(root);
        var issues = new List<ExtractionIssue>();

        var asset = Assets.Collect(objects, Mappings.Value, issues).Single(asset => asset.Id == 42);

        if (clear) Assert.Empty(asset.InventoryRootNames);
        else
        {
            var evidence = Assert.Single(asset.InventoryRootNames).Match.Root;
            Assert.Equal(root.GetPathName(), evidence.Path);
            Assert.Equal(template.GetPathName(), evidence.DefinedAt);
        }
        Assert.Empty(issues);
    }

    [Fact]
    public void DuplicateRootFieldsAreRejectedEvenWhenTheyReferToTheSameSlot()
    {
        var (objects, root, slot, _) = Fixture();
        Add(root, "stashSLOT", Reference(slot));
        var issues = new List<ExtractionIssue>();

        var assets = Assets.Collect(objects, Mappings.Value, issues);

        Assert.Empty(assets.SelectMany(asset => asset.InventoryRootNames));
        Assert.Contains(issues, issue => issue.Message.Contains("Ambiguous property"));
    }

    [Fact]
    public void ObservationsSurvivePackageReleaseAndOrderChanges()
    {
        var (objects, root, _, _) = Fixture();
        var issues = new List<ExtractionIssue>();
        var collector = new CatalogCollector(Mappings.Value, _ => throw new InvalidOperationException(), issues);
        collector.Observe(root);
        foreach (var source in objects.AsEnumerable().Reverse()) collector.Observe(source);
        foreach (var source in objects) source.Properties.Clear();

        var asset = collector.Complete().Single(asset => asset.Id == 44);

        Assert.Equal("Visible category", Text.Read(asset, issues).Name?.Source);
        Assert.Single(asset.InventoryRootNames);
        Assert.Empty(issues);
    }

    [Fact]
    public void InvalidMembershipCannotPublishPartialAllowedContainerNames()
    {
        var (objects, _, _, _) = Fixture();
        var malformed = Item("Malformed", 46, "Container.Child");
        malformed.Properties.Single(property => property.Name.Text == "Tags").Tag = new Int64Property(3);
        objects.Add(malformed);
        var issues = new List<ExtractionIssue>();

        var assets = Assets.Collect(objects, Mappings.Value, issues);

        Assert.Empty(assets.Single(asset => asset.Id == 44).InventoryRootNames);
        Assert.Single(assets.Single(asset => asset.Id == 43).InventoryRootNames);
        Assert.Contains(issues, issue => issue.Message.Contains("unreadable container items"));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void DirectClassDefaultReferencesCannotProvideAssetNames(bool defaultContainer)
    {
        var (objects, _, slot, container) = Fixture();
        (defaultContainer ? container : slot).Flags |= EObjectFlags.RF_ClassDefaultObject;
        var issues = new List<ExtractionIssue>();

        var assets = Assets.Collect(objects, Mappings.Value, issues);

        Assert.Empty(assets.SelectMany(asset => asset.InventoryRootNames));
        Assert.Contains(issues, issue => issue.Message.Contains("class-default object"));
    }

    [Fact]
    public void LocalOnlyContainerDefinitionsDoNotInventIdsOrBlockTheQuery()
    {
        var (objects, _, _, _) = Fixture();
        objects.Add(Object("LocalOnly", "InventoryContainerItemDataAsset"));
        var issues = new List<ExtractionIssue>();

        var assets = Assets.Collect(objects, Mappings.Value, issues);

        Assert.Equal(3, assets.SelectMany(asset => asset.InventoryRootNames).Count());
        Assert.Empty(issues);
    }

    private static (List<UObject> Objects, UObject Root, UObject Slot, UObject Default) Fixture(string field = "StashSlot", string type = "Stash")
    {
        var item = Item("OpaqueA", 43, "Container");
        var slot = Object("OpaqueB", "InventoryContainerSlotDataAsset",
            ("PersistenceDataAsset", Reference(Identity(42))), ("DefaultContainer", Reference(item)),
            ("AllowedContainersQuery", Query()));
        var root = Object("OpaqueC", "InventoryTreeRootAsset", (field, Reference(slot)));
        return ([root, slot, item, Item("OpaqueD", 44, "Container.Child"), Item("OpaqueE", 45, "ContainerElse"),
            Label("OpaqueF", type, "Visible category")], root, slot, item);
    }

    private static UObject Identity(long id) => Object("Identity" + id, "PersistenceDataAsset", ("AssetId", new Int64Property(id)));
    private static UObject Item(string name, long id, string tag) => Object(name, "InventoryContainerItemDataAsset",
        ("PersistenceDataAsset", Reference(Identity(id))),
        ("Tags", new StructProperty(new FScriptStruct(new FGameplayTagContainer([new FGameplayTag(tag)])))));
    private static UObject Label(string name, string type, string text) => Object(name, "UIInventoryContainerMetaDataItem",
        ("ContainerType", new EnumProperty(new FName("ENewInventoryContainerType::" + type))),
        ("ContainerName", new TextProperty(new FText("Categories", text, text))));
    private static StructProperty Query() => new(new FScriptStruct(new FStructFallback([
        new FPropertyTag { Name = "TagDictionary", Tag = new ArrayProperty(new UScriptArray([
            new StructProperty(new FScriptStruct(new FGameplayTag("Container")))], "StructProperty")) },
        new FPropertyTag { Name = "QueryTokenStream", Tag = new ArrayProperty(new UScriptArray(
            new byte[] { 0, 1, 1, 1, 0 }.Select(token => (FPropertyTagType)new ByteProperty(token)).ToList(), "ByteProperty")) }
    ])));
    private static ObjectProperty Reference(UObject source) => new(new FPackageIndex(new FixturePackage(source), 1));
    private static void Add(UObject source, string name, FPropertyTagType value) => source.Properties.Add(new FPropertyTag { Name = name, Tag = value });
    private static UObject Object(string name, string type, params (string Name, FPropertyTagType Value)[] properties) =>
        new(properties.Select(property => new FPropertyTag { Name = property.Name, Tag = property.Value }).ToList())
        { Name = name, Class = new ResolvedLoadedObject(new UScriptClass(type)) };
}
