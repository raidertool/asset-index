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
            ("PersistenceDataAsset", new ObjectProperty(new FPackageIndex(new FixturePackage(persistence), 1))));
        var issues = new List<ExtractionIssue>();

        var asset = Assert.Single(Assets.Collect([persistence, metadata], Mappings.Value, issues));

        Assert.Equal(-42, asset.Id);
        Assert.Equal(persistence.GetPathName(), Assert.Single(asset.Definitions).Reference.Path);
        Assert.Equal(metadata.GetPathName(), Assert.Single(asset.Metadata).Reference.Path);
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

    [Theory]
    [InlineData("bOverrideItemAssetId", "OverrideItemAssetId")]
    [InlineData("boverrideitemassetid", "overrideitemassetid")]
    public void ItemOverrideWinsWithoutLoadingUnusedPersistenceReference(string enabledField, string idField)
    {
        var item = Object("Item", "ItemDataAsset",
            (enabledField, new BoolProperty(true)),
            (idField, new Int64Property(-2144213258)),
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
        var package = new FixturePackage(persistence);
        var item = Object("ReadableItem", "ItemDataAsset",
            ("bOverrideItemAssetId", new BoolProperty(false)),
            ("OverrideItemAssetId", new Int64Property(99)),
            ("PersistenceDataAsset", new ObjectProperty(new FPackageIndex(package, 1))));
        var issues = new List<ExtractionIssue>();

        var asset = Assert.Single(Assets.Collect([persistence, item], Mappings.Value, issues));

        Assert.Equal(42, asset.Id);
        Assert.Equal(["Persistence", "ReadableItem"], asset.Definitions.Select(source => source.Reference.Name));
        Assert.Empty(issues);
    }

    [Fact]
    public void StatsMetadataUsesItsNamedReference()
    {
        var target = Object("Raider", "PlayerStatsRaiderTargetDataAsset", ("AssetId", new Int64Property(995408715)));
        var package = new FixturePackage(target);
        var metadata = Object("TargetUI", "UIPlayerStatsRaiderTargetMetaDataItem",
            ("PlayerStatsRaiderTargetDataAsset", new ObjectProperty(new FPackageIndex(package, 1))));
        var issues = new List<ExtractionIssue>();

        var asset = Assert.Single(Assets.Collect([metadata], Mappings.Value, issues));

        Assert.Equal(target.GetPathName(), Assert.Single(asset.Definitions).Reference.Path);
        Assert.Equal(metadata.GetPathName(), Assert.Single(asset.Metadata).Reference.Path);
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

        Assert.Equal(["First", "Second"], forward.Definitions.Select(source => source.Reference.Name));
        Assert.Equal(forward.Definitions.Select(source => source.Reference), reverse.Definitions.Select(source => source.Reference));
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
        var package = new FixturePackage(persistence);
        var definition = Object("Definition", type,
            ("PersistenceDataAsset", new ObjectProperty(new FPackageIndex(package, 1))));
        var issues = new List<ExtractionIssue>();

        var asset = Assert.Single(Assets.Collect([definition], Mappings.Value, issues));

        Assert.Equal(42, asset.Id);
        Assert.Equal(["Definition", "Identity"], asset.Definitions.Select(source => source.Reference.Name));
        Assert.Empty(issues);
    }

    [Fact]
    public void MetadataCanReferToGenericQuestDefinition()
    {
        var persistence = Object("Identity", "PersistenceDataAsset", ("AssetId", new Int64Property(42)));
        var persistencePackage = new FixturePackage(persistence);
        var quest = Object("Quest", "QuestDefinition",
            ("PersistenceDataAsset", new ObjectProperty(new FPackageIndex(persistencePackage, 1))));
        var questPackage = new FixturePackage(quest);
        var metadata = Object("QuestUI", "UIQuestObjectiveParameterMetaDataItem",
            ("Asset", new ObjectProperty(new FPackageIndex(questPackage, 1))));
        var issues = new List<ExtractionIssue>();

        var asset = Assert.Single(Assets.Collect([metadata], Mappings.Value, issues));

        Assert.Equal(42, asset.Id);
        Assert.Equal(["Identity", "Quest"], asset.Definitions.Select(source => source.Reference.Name));
        Assert.Equal(metadata.GetPathName(), Assert.Single(asset.Metadata).Reference.Path);
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
        Assert.Equal(metadata.GetPathName(), Assert.Single(asset.Metadata).Reference.Path);
        Assert.Contains("retaining its explicit ID", Assert.Single(issues).Message);
    }

    [Theory]
    [InlineData("FakeInventoryServiceItemData", "PersistenceDataAsset")]
    [InlineData("QuestReward", "Item")]
    public void StructSchemasCannotBeTreatedAsObjectClasses(string type, string field)
    {
        var target = Object("Identity", "PersistenceDataAsset", ("AssetId", new Int64Property(99)));
        var package = new FixturePackage(target);
        var payload = Object("Payload", type, (field, new ObjectProperty(new FPackageIndex(package, 1))));
        var issues = new List<ExtractionIssue>();

        Assert.Empty(Assets.Collect([payload], Mappings.Value, issues));
        Assert.Contains(issues, issue => issue.Stage == "asset" && issue.Message.Contains("ends before Object"));
    }

    [Fact]
    public void UnrelatedObjectReferencesDoNotCreateDefinitions()
    {
        var identity = Object("Identity", "PersistenceDataAsset", ("AssetId", new Int64Property(99)));
        var unrelated = Object("Unrelated", "Object",
            ("PersistenceDataAsset", new ObjectProperty(new FPackageIndex(new FixturePackage(identity), 1))));
        var issues = new List<ExtractionIssue>();

        Assert.Empty(Assets.Collect([unrelated], Mappings.Value, issues));
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
            Tag = new ObjectProperty(new FPackageIndex(new FixturePackage(second), 1))
        });
        second.Properties.Add(new FPropertyTag
        {
            Name = "PersistenceDataAsset",
            Tag = new ObjectProperty(new FPackageIndex(new FixturePackage(first), 1))
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
            Name = "PersistenceDataAsset",
            Tag = new ObjectProperty(new FPackageIndex((IPackage)null!, 0))
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

    [Fact]
    public void NestedCatalogProjectionDoesNotDecodeOuterBodies()
    {
        var package = new CrawlerPackage("Plugin/Map.umap", "/Plugin/Map", new CrawlerExport("Map", "World"),
            new CrawlerExport("Actor", "Actor") { OuterIndex = 0 },
            new CrawlerExport("Quest", "QuestDefinition")
            {
                OuterIndex = 1,
                OnLoad = value =>
                {
                    value.Properties.Add(new FPropertyTag
                    {
                        Name = "PersistenceDataAsset",
                        Tag = new ObjectProperty(new FPackageIndex(
                        new FixturePackage(Object("Identity", "PersistenceDataAsset", ("AssetId", new Int64Property(42)))), 1))
                    });
                    value.Properties.Add(new FPropertyTag { Name = "Title", Tag = new TextProperty(new CUE4Parse.UE4.Objects.Core.i18N.FText("Nested quest")) });
                }
            });
        var source = package.ResolvePackageIndex(new FPackageIndex(package, 3))!.Object!.Value;
        var issues = new List<ExtractionIssue>();

        var asset = Assert.Single(Assets.Collect([source], Mappings.Value, issues));

        Assert.Equal("Nested quest", Text.Read(asset, issues).Name?.Source);
        Assert.Contains(asset.Definitions, value => value.Reference.Path == "/Plugin/Map.Map:Actor.Quest");
        Assert.Equal([2], package.BodyReads);
        Assert.Empty(issues);
    }

    [Fact]
    public void UnassociatedSourcesDoNotProduceTextOrImageFailures()
    {
        var source = Object("LocalQuest", "QuestDefinition", ("Title", new Int64Property(12)),
            ("Icon", new ObjectProperty(new FPackageIndex((IPackage)null!, 1))));
        var issues = new List<ExtractionIssue>();

        Assert.Empty(Assets.Collect([source], Mappings.Value, issues));

        Assert.Empty(issues);
    }

    [Fact]
    public void ExplicitlyReferencedClassDefaultsStillSupplyIdentityAndText()
    {
        var identity = Object("DefaultIdentity", "PersistenceDataAsset", ("AssetId", new Int64Property(42)));
        identity.Flags |= EObjectFlags.RF_ClassDefaultObject;
        var source = Object("Quest", "QuestDefinition", ("PersistenceDataAsset", new ObjectProperty(new FPackageIndex(new FixturePackage(identity), 1))),
            ("Title", new TextProperty(new CUE4Parse.UE4.Objects.Core.i18N.FText("Quest label"))));
        var issues = new List<ExtractionIssue>();

        var asset = Assert.Single(Assets.Collect([identity, source], Mappings.Value, issues));

        Assert.Equal(42, asset.Id);
        Assert.Contains(asset.Definitions, value => value.Reference.Path == identity.GetPathName());
        Assert.Equal("Quest label", Text.Read(asset, issues).Name?.Source);
        Assert.Empty(issues);
    }

    [Fact]
    public void CollectorDoesNotRetainDecodedSourcesTemplatesOrRuntimeDeclarations()
    {
        var issues = new List<ExtractionIssue>();
        var (collector, references) = ObserveDetached(issues);
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        Assert.All(references, reference => Assert.False(reference.IsAlive));
        var asset = Assert.Single(collector.Complete());
        Assert.Equal("Inherited title", Text.Read(asset, issues).Name?.Source);
        Assert.Equal("Template", Assert.Single(Text.Read(asset, issues).Candidates).DefinedAt);
        Assert.Empty(issues);
        GC.KeepAlive(collector);
    }

    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    private static (CatalogCollector Collector, WeakReference[] References) ObserveDetached(List<ExtractionIssue> issues)
    {
        var identity = Object("Identity", "PersistenceDataAsset", ("AssetId", new Int64Property(42)));
        var template = Object("Template", "QuestDefinition",
            ("Title", new TextProperty(new CUE4Parse.UE4.Objects.Core.i18N.FText("Inherited title"))));
        var source = Object("Quest", "QuestDefinition",
            ("PersistenceDataAsset", new ObjectProperty(new FPackageIndex(new FixturePackage(identity), 1))));
        source.Template = new ResolvedLoadedObject(template);
        var declaration = RuntimeClassFixture.Derive(source);
        var collector = new CatalogCollector(Mappings.Value, _ => throw new InvalidOperationException("No images expected."), issues);
        collector.Observe(source);
        return (collector, [new(source), new(identity), new(template), new(declaration)]);
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
            Class = new ResolvedLoadedObject(new UScriptClass(type))
        };
    }

}
