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
    public void DiscoversNonItemPersistenceTypesFromMappings(string type)
    {
        var source = Object("Target", type, ("AssetId", new Int64Property(995408715)));
        var issues = new List<ExtractionIssue>();

        var asset = Assert.Single(Assets.Collect([source], Mappings.Value, issues));

        Assert.Equal(995408715, asset.Id);
        Assert.Empty(issues);
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
    public void DisabledOverrideUsesReferencedIdAndPrefersItemName()
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
        Assert.Equal("ReadableItem", asset.Name);
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

        Assert.Same(target, asset.Definition);
        Assert.Same(metadata, Assert.Single(asset.Metadata));
        Assert.Empty(issues);
    }

    [Fact]
    public void ConflictingDefinitionsAreVisibleAndDoNotDependOnDiscoveryOrder()
    {
        var first = Object("First", "ItemDataAsset", ("bOverrideItemAssetId", new BoolProperty(true)),
            ("OverrideItemAssetId", new Int64Property(42)));
        var second = Object("Second", "ItemDataAsset", ("bOverrideItemAssetId", new BoolProperty(true)),
            ("OverrideItemAssetId", new Int64Property(42)));
        var forwardIssues = new List<ExtractionIssue>();
        var reverseIssues = new List<ExtractionIssue>();

        var forward = Assert.Single(Assets.Collect([first, second], Mappings.Value, forwardIssues));
        var reverse = Assert.Single(Assets.Collect([second, first], Mappings.Value, reverseIssues));

        Assert.Equal(forward.Name, reverse.Name);
        Assert.Contains("Multiple definitions", Assert.Single(forwardIssues).Message);
        Assert.Equal(forwardIssues, reverseIssues);
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
    [InlineData("ItemDataAsset")]
    [InlineData("UICurrencyMetaDataItem")]
    public void MissingIdentityIsReportedForIdentityBearingClasses(string type)
    {
        var issues = new List<ExtractionIssue>();

        Assert.Empty(Assets.Collect([Object("Unresolved", type)], Mappings.Value, issues));
        Assert.Contains("no resolvable", Assert.Single(issues).Message);
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
