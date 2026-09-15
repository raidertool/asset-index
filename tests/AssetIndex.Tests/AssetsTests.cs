using CUE4Parse.MappingsProvider;
using CUE4Parse.MappingsProvider.Usmap;
using CUE4Parse.UE4.Assets;
using CUE4Parse.UE4.Assets.Exports;
using CUE4Parse.UE4.Assets.Objects;
using CUE4Parse.UE4.Assets.Objects.Properties;
using CUE4Parse.UE4.Objects.UObject;

namespace AssetIndex.Tests;

public sealed class AssetsTests
{
    private static readonly Lazy<TypeMappings> Mappings = new(() =>
        new FileUsmapTypeMappingsProvider(Path.Combine(AppContext.BaseDirectory, "mappings", "ArcRaiders.usmap"))
            .MappingsForGame!);

    [Theory]
    [InlineData("PlayerStatsRaiderTargetDataAsset")]
    [InlineData("WorldQuestDataAsset")]
    [InlineData("XPEventCategoryDataAsset")]
    [InlineData("OptionalPersistenceDataAsset")]
    public void DiscoversNonItemPersistenceTypesFromMappings(string type)
    {
        var source = Object("Target", type, ("AssetId", new Int64Property(995408715)));
        var issues = new List<ExtractionIssue>();

        var asset = Assert.Single(Assets.Collect([source], Mappings.Value, issues));

        Assert.Equal(995408715, asset.Id);
        Assert.Empty(issues);
    }

    [Fact]
    public void MapConditionMetadataUsesOptionalPersistenceIdentity()
    {
        var persistence = Object("Condition", "OptionalPersistenceDataAsset", ("AssetId", new Int64Property(-42)));
        var metadata = Object("ConditionUI", "UIMapConditionMetaDataItem",
            ("PersistenceDataAsset", new ObjectProperty(new FPackageIndex(new TestPackage(persistence), 1))));
        var issues = new List<ExtractionIssue>();

        var asset = Assert.Single(Assets.Collect([persistence, metadata], Mappings.Value, issues));

        Assert.Equal(-42, asset.Id);
        Assert.Same(persistence, Assert.Single(asset.Definitions));
        Assert.Same(metadata, Assert.Single(asset.Metadata));
        Assert.Empty(issues);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void OptionalPersistenceWithoutAValidIdIsReported(bool hasZero)
    {
        var source = Object("Invalid", "OptionalPersistenceDataAsset");
        if (hasZero) source.Properties.Add(new FPropertyTag { Name = "AssetId", Tag = new Int64Property(0) });
        var issues = new List<ExtractionIssue>();

        Assert.Empty(Assets.Collect([source], Mappings.Value, issues));
        Assert.Equal("asset", Assert.Single(issues).Stage);
    }

    [Fact]
    public void ItemOverrideWinsWithoutLoadingUnusedPersistenceReference()
    {
        var item = Object("Item", "ItemDataAsset",
            ("bOverrideItemAssetId", new BoolProperty(true)),
            ("OverrideItemAssetId", new Int64Property(-2144213258)),
            ("PersistenceDataAsset", new ObjectProperty(new FPackageIndex((IPackage)null!, 1))));
        var issues = new List<ExtractionIssue>();

        var asset = Assert.Single(Assets.Collect([item], Mappings.Value, issues));

        Assert.Equal(-2144213258, asset.Id);
        Assert.Empty(issues);
    }

    [Fact]
    public void DisabledOverrideUsesReferencedIdAndPreservesBothDefinitions()
    {
        var persistence = Object("Persistence", "PersistenceDataAsset", ("AssetId", new Int64Property(42)));
        var package = new TestPackage(persistence);
        var item = Object("ReadableItem", "ItemDataAsset",
            ("bOverrideItemAssetId", new BoolProperty(false)),
            ("OverrideItemAssetId", new Int64Property(99)),
            ("PersistenceDataAsset", new ObjectProperty(new FPackageIndex(package, 1))));
        var issues = new List<ExtractionIssue>();

        var asset = Assert.Single(Assets.Collect([persistence, item], Mappings.Value, issues));

        Assert.Equal(42, asset.Id);
        Assert.Equal(["Persistence", "ReadableItem"], asset.Definitions.Select(source => source.Name));
        Assert.Empty(issues);
    }

    [Fact]
    public void StatsMetadataUsesItsNamedReference()
    {
        var target = Object("Raider", "PlayerStatsRaiderTargetDataAsset", ("AssetId", new Int64Property(995408715)));
        var package = new TestPackage(target);
        var metadata = Object("TargetUI", "UIPlayerStatsRaiderTargetMetaDataItem",
            ("PlayerStatsRaiderTargetDataAsset", new ObjectProperty(new FPackageIndex(package, 1))));
        var issues = new List<ExtractionIssue>();

        var asset = Assert.Single(Assets.Collect([metadata], Mappings.Value, issues));

        Assert.Same(target, Assert.Single(asset.Definitions));
        Assert.Same(metadata, Assert.Single(asset.Metadata));
        Assert.Empty(issues);
    }

    [Fact]
    public void SharedIdDefinitionsArePreservedWithoutDependingOnDiscoveryOrder()
    {
        var first = Object("First", "ItemDataAsset", ("bOverrideItemAssetId", new BoolProperty(true)),
            ("OverrideItemAssetId", new Int64Property(42)));
        var second = Object("Second", "ItemDataAsset", ("bOverrideItemAssetId", new BoolProperty(true)),
            ("OverrideItemAssetId", new Int64Property(42)));
        var forwardIssues = new List<ExtractionIssue>();
        var reverseIssues = new List<ExtractionIssue>();

        var forward = Assert.Single(Assets.Collect([first, second, first], Mappings.Value, forwardIssues));
        var reverse = Assert.Single(Assets.Collect([second, first], Mappings.Value, reverseIssues));

        Assert.Equal(["First", "Second"], forward.Definitions.Select(source => source.Name));
        Assert.Equal(forward.Definitions, reverse.Definitions);
        Assert.Empty(forwardIssues);
        Assert.Empty(reverseIssues);
    }

    [Theory]
    [InlineData("QuestDefinition")]
    [InlineData("BattlepassDataAsset")]
    [InlineData("PioneerMatchmakableLevelDataAsset")]
    [InlineData("SessionModifierDataAsset")]
    [InlineData("StoreOfferDataAsset")]
    [InlineData("ItemWithQualityRedirectorAsset")]
    [InlineData("WeaponRedirectorAsset")]
    public void GenericDataAssetUsesExactPersistenceLink(string type)
    {
        var persistence = Object("Identity", "PersistenceDataAsset", ("AssetId", new Int64Property(42)));
        var package = new TestPackage(persistence);
        var definition = Object("Definition", type,
            ("PersistenceDataAsset", new ObjectProperty(new FPackageIndex(package, 1))));
        var issues = new List<ExtractionIssue>();

        var asset = Assert.Single(Assets.Collect([definition], Mappings.Value, issues));

        Assert.Equal(42, asset.Id);
        Assert.Equal(["Definition", "Identity"], asset.Definitions.Select(source => source.Name));
        Assert.Empty(issues);
    }

    [Fact]
    public void MetadataCanReferToGenericQuestDefinition()
    {
        var persistence = Object("Identity", "PersistenceDataAsset", ("AssetId", new Int64Property(42)));
        var persistencePackage = new TestPackage(persistence);
        var quest = Object("Quest", "QuestDefinition",
            ("PersistenceDataAsset", new ObjectProperty(new FPackageIndex(persistencePackage, 1))));
        var questPackage = new TestPackage(quest);
        var metadata = Object("QuestUI", "UIQuestObjectiveParameterMetaDataItem",
            ("Asset", new ObjectProperty(new FPackageIndex(questPackage, 1))));
        var issues = new List<ExtractionIssue>();

        var asset = Assert.Single(Assets.Collect([metadata], Mappings.Value, issues));

        Assert.Equal(42, asset.Id);
        Assert.Equal(["Identity", "Quest"], asset.Definitions.Select(source => source.Name));
        Assert.Same(metadata, Assert.Single(asset.Metadata));
        Assert.Empty(issues);
    }

    [Fact]
    public void MetadataOverrideIdSurvivesWithoutADefinition()
    {
        var metadata = Object("KnownUI", "UIGameplayItemMetaDataItem",
            ("bOverrideAssetId", new BoolProperty(true)),
            ("OverrideAssetId", new Int64Property(-42)),
            ("PersistenceDataAsset", new ObjectProperty(new FPackageIndex((IPackage)null!, 1))));
        var issues = new List<ExtractionIssue>();

        var asset = Assert.Single(Assets.Collect([metadata], Mappings.Value, issues));

        Assert.Equal(-42, asset.Id);
        Assert.Empty(asset.Definitions);
        Assert.Same(metadata, Assert.Single(asset.Metadata));
        Assert.Contains("retaining its explicit ID", Assert.Single(issues).Message);
    }

    [Theory]
    [InlineData("FakeInventoryServiceItemData", "PersistenceDataAsset")]
    [InlineData("QuestReward", "Item")]
    public void RuntimeAndRewardReferencesDoNotCreateDefinitions(string type, string field)
    {
        var target = Object("Identity", "PersistenceDataAsset", ("AssetId", new Int64Property(99)));
        var package = new TestPackage(target);
        var payload = Object("Payload", type, (field, new ObjectProperty(new FPackageIndex(package, 1))));
        var issues = new List<ExtractionIssue>();

        Assert.Empty(Assets.Collect([payload], Mappings.Value, issues));
        Assert.Empty(issues);
    }

    [Fact]
    public void PersistenceLinkDoesNotTraverseACycleOfDefinitions()
    {
        var first = Object("First", "QuestDefinition");
        var second = Object("Second", "QuestDefinition");
        first.Properties.Add(new FPropertyTag
        {
            Name = "PersistenceDataAsset",
            Tag = new ObjectProperty(new FPackageIndex(new TestPackage(second), 1))
        });
        second.Properties.Add(new FPropertyTag
        {
            Name = "PersistenceDataAsset",
            Tag = new ObjectProperty(new FPackageIndex(new TestPackage(first), 1))
        });
        var issues = new List<ExtractionIssue>();

        Assert.Empty(Assets.Collect([first], Mappings.Value, issues));
        Assert.Contains("no resolvable persistence asset", Assert.Single(issues).Message);
    }

    [Fact]
    public void EnabledOverrideWithoutValueIsAnExtractionFailure()
    {
        var source = Object("Broken", "ItemDataAsset", ("bOverrideItemAssetId", new BoolProperty(true)));
        var issues = new List<ExtractionIssue>();

        Assert.Empty(Assets.Collect([source], Mappings.Value, issues));
        Assert.Contains("OverrideItemAssetId is absent", Assert.Single(issues).Message);
    }

    [Theory]
    [InlineData("UICurrencyMetaDataItem")]
    public void MissingIdentityIsReportedForIdentityBearingClasses(string type)
    {
        var issues = new List<ExtractionIssue>();

        Assert.Empty(Assets.Collect([Object("Unresolved", type)], Mappings.Value, issues));
        Assert.Contains("no resolvable", Assert.Single(issues).Message);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void LocalDefinitionsWithoutAnIdentityLinkAreNotAssignedIds(bool explicitNull)
    {
        var source = Object("LocalModifier", "SessionModifierDataAsset");
        if (explicitNull) source.Properties.Add(new FPropertyTag
        {
            Name = "PersistenceDataAsset", Tag = new ObjectProperty(new FPackageIndex((IPackage)null!, 0))
        });
        var issues = new List<ExtractionIssue>();
        Assert.Empty(Assets.Collect([source], Mappings.Value, issues));
        Assert.Empty(issues);
    }

    [Fact]
    public void PureUiLabelsDoNotRequireGameIds()
    {
        var issues = new List<ExtractionIssue>();

        Assert.Empty(Assets.Collect([Object("Filter", "UIInventoryFilterMetaDataItem")], Mappings.Value, issues));
        Assert.Empty(issues);
    }

    [Fact]
    public void TemplateLookupPreservesExplicitFalseAndNull()
    {
        var parent = Object("Parent", "ItemDataAsset", ("Enabled", new BoolProperty(true)),
            ("Value", new Int64Property(42)),
            ("Reference", new ObjectProperty(new FPackageIndex((IPackage)null!, 1))));
        var child = Object("Child", "ItemDataAsset", ("Enabled", new BoolProperty(false)),
            ("Reference", new ObjectProperty(new FPackageIndex())));
        child.Template = new ResolvedLoadedObject(parent);

        Assert.True(Properties.TryGet<bool>(child, "Enabled", out var enabled));
        Assert.False(enabled);
        Assert.True(Properties.TryGet<long>(child, "Value", out var value));
        Assert.Equal(42, value);
        Assert.Null(Properties.Reference(child, "Reference"));
    }

    [Fact]
    public void TemplateCyclesFailWithAnExplanation()
    {
        var first = Object("First", "ItemDataAsset");
        var second = Object("Second", "ItemDataAsset");
        first.Template = new ResolvedLoadedObject(second);
        second.Template = new ResolvedLoadedObject(first);

        Assert.Contains("Template cycle", Assert.Throws<InvalidDataException>(() => Properties.Find(first, "Missing")).Message);
    }

    [Fact]
    public void UnknownClassAncestryIsDistinguishedFromAnUnrelatedClass()
    {
        Assert.Null(Assets.IsA(Mappings.Value, "UnmappedNewClass", "DataAsset"));
        Assert.False(Assets.IsA(Mappings.Value, "Texture2D", "DataAsset"));
    }

    private static UObject Object(string name, string type, params (string Name, FPropertyTagType Value)[] properties)
    {
        return new UObject(properties.Select(property => new FPropertyTag
        {
            Name = property.Name,
            Tag = property.Value
        }).ToList())
        {
            Name = name,
            Class = new ResolvedLoadedObject(new UObject { Name = type })
        };
    }

    private sealed class TestPackage(UObject export) : AbstractUePackage("Fixture", null)
    {
        public override FPackageFileSummary Summary => throw new NotSupportedException();
        public override FNameEntrySerialized[] NameMap => [];
        public override int ImportMapLength => 0;
        public override int ExportMapLength => 1;
        public override int GetExportIndex(string name, StringComparison comparisonType = StringComparison.Ordinal) => 0;
        public override ResolvedObject? ResolvePackageIndex(FPackageIndex? index) => index is { Index: 1 }
            ? new ResolvedLoadedObject(export)
            : null;
    }
}
