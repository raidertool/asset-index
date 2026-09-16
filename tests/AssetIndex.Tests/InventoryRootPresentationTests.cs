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
    public void ReviewedRootRulesFollowTypedReferencesAndParentTagMembership(string field, string type)
    {
        var issues = new List<ExtractionIssue>();

        var assets = Assets.Collect(Fixture(field, type), Mappings.Value, issues);

        Assert.Equal(new long[] { 42, 43, 44 }, assets.Select(asset => asset.Id));
        foreach (var asset in assets)
        {
            Assert.Contains(asset.Definitions, source => source.Reference.Class is
                "InventoryContainerSlotDataAsset" or "InventoryContainerItemDataAsset");
            var name = Assert.Single(asset.InventoryRootNames);
            Assert.Equal(field, name.Match.RootField);
            Assert.Equal("ENewInventoryContainerType::" + type, name.Match.ContainerType);
            Assert.Equal(asset.Id switch { 42 => "container-slot", 43 => "default-container", _ => "allowed-container" }, name.Match.Role);
            var text = Text.Read(asset, issues);
            Assert.Equal("Visible category", text.Name?.Source);
            Assert.Equal("inventory-root", Assert.Single(text.Candidates).SourceKind);
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
        Assert.Contains(Text.Read(asset, issues).Candidates, candidate => candidate.SourceKind == "inventory-root");
        Assert.Empty(issues);
    }

    [Fact]
    public void ConflictingCategoryLabelsRemainCandidatesWithoutAPrimaryName()
    {
        var objects = Fixture("StashSlot", "Stash");
        objects.Add(Object("OtherLabel", "UIInventoryContainerMetaDataItem",
            ("ContainerType", new EnumProperty(new FName("ENewInventoryContainerType::Stash"))),
            ("ContainerName", new TextProperty(new FText("Other category")))));
        var issues = new List<ExtractionIssue>();

        var text = Text.Read(Assets.Collect(objects, Mappings.Value, issues).Single(asset => asset.Id == 42), issues);

        Assert.Null(text.Name);
        Assert.Equal(2, text.Candidates.Count);
        Assert.Single(text.Notices);
        Assert.Empty(issues);
    }

    [Fact]
    public void ExplicitNullRootOverridesInheritedReference()
    {
        var objects = Fixture("StashSlot", "Stash");
        var root = objects[0];
        var inherited = Object("InheritedRoot", "InventoryTreeRootAsset", ("StashSlot", root.Properties[0].Tag!));
        root.Template = new ResolvedLoadedObject(inherited);
        root.Properties[0].Tag = new ObjectProperty(new FPackageIndex());
        var issues = new List<ExtractionIssue>();

        var assets = Assets.Collect(objects, Mappings.Value, issues);

        Assert.All(assets, asset => Assert.Empty(asset.InventoryRootNames));
        Assert.Empty(issues);
    }

    [Theory]
    [InlineData("root")]
    [InlineData("slot")]
    [InlineData("container")]
    [InlineData("metadata")]
    public void RuntimeClassNamesCannotImpersonateNativeRoles(string target)
    {
        var objects = Fixture("StashSlot", "Stash");
        var source = objects[target switch { "root" => 0, "slot" => 1, "container" => 2, _ => 4 }];
        var nativeName = source.ExportType;
        source.Class = new ResolvedLoadedObject(new UScriptClass("DataAsset"));
        RuntimeClassFixture.Derive(source, nativeName);
        var issues = new List<ExtractionIssue>();

        var assets = Assets.Collect(objects, Mappings.Value, issues);

        Assert.All(assets, asset => Assert.Empty(asset.InventoryRootNames));
        if (target is "slot" or "container") Assert.Contains(issues, issue => issue.Message.StartsWith("Expected InventoryContainer"));
    }

    [Theory]
    [InlineData("StashSlot")]
    [InlineData("AllowedContainersQuery")]
    [InlineData("DefaultContainer")]
    [InlineData("Tags")]
    public void MalformedSelectedFieldsCannotProduceRootLabels(string field)
    {
        var objects = Fixture("StashSlot", "Stash");
        var source = field switch { "StashSlot" => objects[0], "Tags" => objects[2], _ => objects[1] };
        source.Properties.Single(property => property.Name.Text == field).Tag = new Int64Property(123);
        var issues = new List<ExtractionIssue>();

        var assets = Assets.Collect(objects, Mappings.Value, issues);

        Assert.All(assets, asset => Assert.Empty(asset.InventoryRootNames));
        Assert.NotEmpty(issues);
    }

    [Theory]
    [InlineData("StashSlot")]
    [InlineData("AllowedContainersQuery")]
    [InlineData("DefaultContainer")]
    public void DuplicateSelectedFieldsAreAmbiguous(string field)
    {
        var objects = Fixture("StashSlot", "Stash");
        var source = field == "StashSlot" ? objects[0] : objects[1];
        var selected = source.Properties.Single(property => property.Name.Text == field);
        source.Properties.Add(new FPropertyTag { Name = field.ToLowerInvariant(), Tag = selected.Tag });
        var issues = new List<ExtractionIssue>();

        var assets = Assets.Collect(objects, Mappings.Value, issues);

        Assert.All(assets, asset => Assert.Empty(asset.InventoryRootNames));
        Assert.Contains(issues, issue => issue.Message.Contains("Ambiguous property"));
    }

    [Fact]
    public void MalformedQueryCannotPartiallyAdmitMembers()
    {
        var objects = Fixture("StashSlot", "Stash");
        objects[1].Properties.Single(property => property.Name.Text == "AllowedContainersQuery").Tag = Query([0, 1, 1, 1, 0, 0]);
        var issues = new List<ExtractionIssue>();

        var assets = Assets.Collect(objects, Mappings.Value, issues);

        Assert.All(assets, asset => Assert.Empty(asset.InventoryRootNames));
        Assert.Contains(issues, issue => issue.Stage == "presentation");
    }

    [Fact]
    public void UnreadableEligibleItemsPreventPartialQueryMembership()
    {
        var objects = Fixture("StashSlot", "Stash");
        var invalid = Item("Invalid", 45, "Container");
        invalid.Properties.Single(property => property.Name.Text == "Tags").Tag = new Int64Property(123);
        objects.Add(invalid);
        var issues = new List<ExtractionIssue>();

        var assets = Assets.Collect(objects, Mappings.Value, issues);

        Assert.Single(assets.Single(asset => asset.Id == 42).InventoryRootNames);
        Assert.Single(assets.Single(asset => asset.Id == 43).InventoryRootNames);
        Assert.Empty(assets.Single(asset => asset.Id == 44).InventoryRootNames);
        Assert.Contains(issues, issue => issue.Message.Contains("unreadable container items"));
    }

    [Fact]
    public void LocalOnlyAndClassDefaultItemsDoNotAcquireLabels()
    {
        var objects = Fixture("StashSlot", "Stash");
        var local = Item("Local", 45, "Container");
        local.Properties.RemoveAll(property => property.Name.Text == "PersistenceDataAsset");
        var classDefault = Item("ClassDefault", 46, "Container");
        classDefault.Flags |= EObjectFlags.RF_ClassDefaultObject;
        objects.AddRange([local, classDefault]);
        var issues = new List<ExtractionIssue>();

        var assets = Assets.Collect(objects, Mappings.Value, issues);

        Assert.Equal(new long[] { 42, 43, 44 }, assets.Select(asset => asset.Id));
        Assert.Empty(issues);
    }

    [Fact]
    public void QueryMembershipDoesNotUseObjectNames()
    {
        var objects = Fixture("StashSlot", "Stash");
        objects.Add(Item("StashContainer", 45, "Unrelated"));
        var issues = new List<ExtractionIssue>();

        var assets = Assets.Collect(objects, Mappings.Value, issues);

        Assert.Empty(assets.Single(asset => asset.Id == 45).InventoryRootNames);
        Assert.Single(assets.Single(asset => asset.Id == 44).InventoryRootNames);
        Assert.Empty(issues);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public void RootLinksCannotNameClassDefaultTargets(int target)
    {
        var objects = Fixture("StashSlot", "Stash");
        objects[target].Flags |= EObjectFlags.RF_ClassDefaultObject;
        var issues = new List<ExtractionIssue>();

        var assets = Assets.Collect(objects, Mappings.Value, issues);

        Assert.All(assets, asset => Assert.Empty(asset.InventoryRootNames));
        Assert.Contains(issues, issue => issue.Message.Contains("class-default object"));
    }

    [Fact]
    public void ExplicitNullDefaultContainerRetainsSlotAndQueryMembership()
    {
        var objects = Fixture("StashSlot", "Stash");
        var slot = objects[1];
        var inherited = Object("InheritedSlot", "InventoryContainerSlotDataAsset",
            ("DefaultContainer", slot.Properties.Single(property => property.Name.Text == "DefaultContainer").Tag!));
        slot.Template = new ResolvedLoadedObject(inherited);
        slot.Properties.Single(property => property.Name.Text == "DefaultContainer").Tag = new ObjectProperty(new FPackageIndex());
        var issues = new List<ExtractionIssue>();

        var assets = Assets.Collect(objects, Mappings.Value, issues);

        Assert.Equal("container-slot", Assert.Single(assets.Single(asset => asset.Id == 42).InventoryRootNames).Match.Role);
        Assert.Equal("allowed-container", Assert.Single(assets.Single(asset => asset.Id == 43).InventoryRootNames).Match.Role);
        Assert.Empty(issues);
    }

    [Fact]
    public void NativeDerivedObjectsAndCaseInsensitiveNamesKeepTheSameJoin()
    {
        var objects = Fixture("StashSlot", "Stash");
        foreach (var source in objects) RuntimeClassFixture.Derive(source);
        objects[0].Properties[0].Name = "sTASHsLOT";
        objects[4].Properties[0].Tag = new EnumProperty(new FName("enewinventorycontainertype::stash"));
        var issues = new List<ExtractionIssue>();

        var assets = Assets.Collect(objects, Mappings.Value, issues);

        Assert.All(assets, asset => Assert.Equal("Visible category", Text.Read(asset, issues).Name?.Source));
        Assert.Empty(issues);
    }

    [Fact]
    public void SharedIdentityMembersHaveStableOrderAndDetachedEvidence()
    {
        var objects = Fixture("StashSlot", "Stash");
        objects.Add(Item("AnotherMember", 44, "Container.Child"));
        var issues = new List<ExtractionIssue>();
        var forward = Assets.Collect(objects, Mappings.Value, issues).Single(asset => asset.Id == 44);
        var collector = new CatalogCollector(Mappings.Value, _ => throw new InvalidOperationException("No images expected."), issues);
        foreach (var source in objects.AsEnumerable().Reverse()) collector.Observe(source);
        foreach (var source in objects) source.Properties.Clear();

        var reverse = collector.Complete().Single(asset => asset.Id == 44);

        Assert.Equal(2, forward.InventoryRootNames.Count);
        Assert.Equal(forward.InventoryRootNames.Select(name => name.Match), reverse.InventoryRootNames.Select(name => name.Match));
        Assert.Equal("Visible category", Text.Read(reverse, issues).Name?.Source);
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
    private static StructProperty Query(byte[]? tokens = null) => new(new FScriptStruct(new FStructFallback([
        new FPropertyTag { Name = "TagDictionary", Tag = new ArrayProperty(new UScriptArray([
            new StructProperty(new FScriptStruct(new FGameplayTag("Container")))], "StructProperty")) },
        new FPropertyTag { Name = "QueryTokenStream", Tag = new ArrayProperty(new UScriptArray(
            (tokens ?? [0, 1, 1, 1, 0]).Select(token => (FPropertyTagType)new ByteProperty(token)).ToList(), "ByteProperty")) }
    ])));
    private static ObjectProperty Reference(UObject source) => new(new FPackageIndex(new FixturePackage(source), 1));
    private static UObject Object(string name, string type, params (string Name, FPropertyTagType Value)[] properties) =>
        new(properties.Select(property => new FPropertyTag { Name = property.Name, Tag = property.Value }).ToList())
        { Name = name, Class = new ResolvedLoadedObject(new UScriptClass(type)) };
}
