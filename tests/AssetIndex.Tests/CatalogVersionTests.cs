using System.Runtime.CompilerServices;
using CUE4Parse.Compression;
using CUE4Parse.FileProvider.Objects;
using CUE4Parse.MappingsProvider;
using CUE4Parse.MappingsProvider.Usmap;
using CUE4Parse.UE4.Assets;
using CUE4Parse.UE4.Assets.Exports;
using CUE4Parse.UE4.Assets.Objects;
using CUE4Parse.UE4.Assets.Objects.Properties;
using CUE4Parse.UE4.Objects.Core.i18N;
using CUE4Parse.UE4.Objects.UObject;
using CUE4Parse.UE4.Readers;

namespace AssetIndex.Tests;

public sealed class CatalogVersionTests
{
    private static readonly Lazy<TypeMappings> Mappings = new(() =>
        new FileUsmapTypeMappingsProvider(Path.Combine(AppContext.BaseDirectory, "mappings", "ArcRaiders.usmap")).MappingsForGame!);

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void MetadataReferencesPreserveSamePathDefinitionsFromDifferentVersions(bool reverse)
    {
        var files = new[] { new VersionFile(42, "First"), new VersionFile(43, "Second") };
        using var provider = new VersionProvider();
        var issues = new List<ExtractionIssue>();
        var collector = new CatalogCollector(Mappings.Value, provider.Locate, issues);
        foreach (var file in reverse ? files.Reverse() : files)
            collector.Observe(provider.LoadPackage(file).ExportsLazy[4].Value);

        var assets = collector.Complete();

        Assert.Equal([42L, 43L], assets.Select(asset => asset.Id));
        Assert.Equal(["First", "Second"], assets.Select(asset => Text.Read(asset, issues).Name!.Source));
        Assert.All(assets, asset => Assert.Contains(asset.Definitions, source => source.Reference.Path == "/Game/Shared.Quest"));
        Assert.Empty(issues);
    }

    [Fact]
    public void SamePathMetadataVersionsKeepTheirOwnTextAndPhysicalImageLocations()
    {
        var first = new VersionFile(42, "First");
        var second = new VersionFile(43, "Second");
        using var provider = new VersionProvider();
        var issues = new List<ExtractionIssue>();
        var collector = new CatalogCollector(Mappings.Value, provider.Locate, issues);
        collector.Observe(provider.LoadPackage(first).ExportsLazy[0].Value);
        collector.Observe(provider.LoadPackage(second).ExportsLazy[0].Value);

        var assets = collector.Complete();

        Assert.Equal([42L, 43L], assets.Select(asset => asset.Id));
        Assert.Equal(["First", "Second"], assets.Select(asset => Text.Read(asset, issues).Name!.Source));
        var images = assets.Select(asset => Assert.Single(Assert.Single(asset.Metadata).Images)).ToArray();
        Assert.Equal(images[0].Resource, images[1].Resource);
        Assert.Same(first, images[0].Location!.File);
        Assert.Same(second, images[1].Location!.File);
        Assert.Empty(issues);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ConflictingVersionsWithinOneIdentityAreDiagnosed(bool differentText)
    {
        using var provider = new VersionProvider();
        var issues = new List<ExtractionIssue>();
        var collector = new CatalogCollector(Mappings.Value, provider.Locate, issues);
        collector.Observe(provider.LoadPackage(new VersionFile(42, "First")).ExportsLazy[0].Value);
        collector.Observe(provider.LoadPackage(new VersionFile(42, differentText ? "Second" : "First")).ExportsLazy[0].Value);

        Assert.Single(collector.Complete());

        var issue = Assert.Single(issues);
        Assert.Equal("/Game/Shared.UI", issue.Path);
        Assert.Contains("Conflicting source observations", issue.Message);
        Assert.Contains("42", issue.Message);
    }

    [Fact]
    public void EquivalentReloadsDoNotConflictOrKeepThePreviousPackageAlive()
    {
        var file = new VersionFile(42, "First");
        using var provider = new VersionProvider();
        var issues = new List<ExtractionIssue>();
        var collector = new CatalogCollector(Mappings.Value, provider.Locate, issues);
        var (package, source) = ObserveWeak(collector, provider, file);
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        Assert.False(package.IsAlive);
        Assert.False(source.IsAlive);

        collector.Observe(provider.LoadPackage(file).ExportsLazy[0].Value);
        var asset = Assert.Single(collector.Complete());

        Assert.Equal("First", Text.Read(asset, issues).Name!.Source);
        Assert.Single(asset.Metadata);
        Assert.Empty(issues);
        GC.KeepAlive(collector);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static (WeakReference Package, WeakReference Source) ObserveWeak(CatalogCollector collector,
        PackageProvider provider, GameFile file)
    {
        var package = provider.LoadPackage(file);
        var source = package.ExportsLazy[0].Value;
        collector.Observe(source);
        collector.Observe(source);
        return (new(package), new(source));
    }

    private sealed class VersionProvider() : PackageProvider(Path.GetTempPath())
    {
        protected override IPackage ReadPackage(GameFile file) => new VersionPackage((VersionFile)file, this);
    }

    private sealed class VersionPackage : AbstractUePackage
    {
        public VersionPackage(VersionFile file, VersionProvider provider) : base("/Game/Shared", provider)
        {
            ExportsLazy =
            [
                new(() => Object("UI", "UIGameplayItemMetaDataItem", ("PersistenceDataAsset", Reference(3)),
                    ("ItemName", new TextProperty(new FText("test", file.Label, file.Label))), ("Icon", Reference(2)))),
                new(() => Object("Icon", "Texture2D")),
                new(() => Object("Identity", "PersistenceDataAsset", ("AssetId", new Int64Property(file.Id)))),
                new(() => Object("Quest", "QuestDefinition", ("PersistenceDataAsset", Reference(3)),
                    ("Title", new TextProperty(new FText("test", file.Label, file.Label))))),
                new(() => Object("QuestUI" + file.Id, "UIQuestObjectiveParameterMetaDataItem", ("Asset", Reference(4))))
            ];
        }

        private UObject Object(string name, string type, params (string Name, FPropertyTagType Value)[] fields) => new(
            fields.Select(field => new FPropertyTag { Name = field.Name, PropertyType = field.Value.GetType().Name, Tag = field.Value }).ToList())
        { Name = name, Class = new ResolvedLoadedObject(new UScriptClass(type)), Outer = new ResolvedPackageObject(this) };

        private ObjectProperty Reference(int index) => new(new FPackageIndex(this, index));
        public override FPackageFileSummary Summary => throw new NotSupportedException();
        public override FNameEntrySerialized[] NameMap => [];
        public override int ImportMapLength => 0;
        public override int ExportMapLength => ExportsLazy.Length;
        public override int GetExportIndex(string name, StringComparison comparisonType = StringComparison.Ordinal) => throw new NotSupportedException();
        public override ResolvedObject? ResolvePackageIndex(FPackageIndex? index) => index is { Index: > 0 and <= 5 }
            ? new ResolvedLoadedObject(ExportsLazy[index.Index - 1].Value) : null;
    }

    private sealed class VersionFile(long id, string label) : GameFile("Game/Shared.uasset", 0)
    {
        public long Id => id;
        public string Label => label;
        public override bool IsEncrypted => false;
        public override CompressionMethod CompressionMethod => CompressionMethod.None;
        public override byte[] Read(FByteBulkDataHeader? header = null) => throw new NotSupportedException();
        public override FArchive CreateReader(FByteBulkDataHeader? header = null) => throw new NotSupportedException();
    }
}
