using System.Reflection;
using AssetIndex.Discovery;
using CUE4Parse.Compression;
using CUE4Parse.FileProvider.Objects;
using CUE4Parse.GameTypes.Theia.FileProvider;
using CUE4Parse.MappingsProvider.Usmap;
using CUE4Parse.UE4.Assets;
using CUE4Parse.UE4.Assets.Exports;
using CUE4Parse.UE4.Assets.Objects;
using CUE4Parse.UE4.Objects.UObject;
using CUE4Parse.UE4.Readers;
using CUE4Parse.UE4.Versions;

namespace AssetIndex.Tests;

public sealed class DiscoveryCollisionTests
{
    [Theory]
    [InlineData("Actual", "Actual")]
    [InlineData("Actual", "actual")]
    public void DuplicatePathsKeepBothOriginsAndTheSecondExportsDiagnostics(string firstName, string secondName)
    {
        const string path = "PioneerGame/Content/Selected.uasset";
        var second = new UObject { Name = secondName };
        second.Properties.Add(new FPropertyTag { Name = "UnreadField", Tag = null });
        using var provider = new TestProvider(new TestPackage(new UObject { Name = firstName }, second));
        var evidence = new List<ObjectEvidence>();
        var crawler = new ObjectCrawler(provider, evidence.Add);

        ReadPackage(crawler, path);

        var issues = Issues(crawler);
        var collision = Assert.Single(issues, issue => issue.Message.StartsWith("Duplicate object path"));
        Assert.Equal("decode", collision.Stage);
        Assert.Equal($"{path}#export/1", collision.Path);
        Assert.Contains($"first decoded at {path}#export/0", collision.Message);
        Assert.Contains($"repeated at {path}#export/1", collision.Message);
        Assert.Equal(2, evidence.Count);
        Assert.Contains(issues, issue => issue.Stage == "evidence" && issue.Path.EndsWith("/Properties/0"));
    }

    [Fact]
    public void DistinctPathsRemainSeparateWithoutCollisionDiagnostics()
    {
        using var provider = new TestProvider(new TestPackage(new UObject { Name = "First" }, new UObject { Name = "Second" }));
        var evidence = new List<ObjectEvidence>();
        var crawler = new ObjectCrawler(provider, evidence.Add);

        ReadPackage(crawler, "PioneerGame/Content/Selected.uasset");

        Assert.Empty(Issues(crawler));
        Assert.Equal(2, evidence.Count);
    }

    [Fact]
    public void RepeatedObjectFromAnotherInputRecordsBothPhysicalOrigins()
    {
        const string first = "PioneerGame/Content/Selected.uasset";
        const string second = "PioneerGame/Content/Alias.uasset";
        using var provider = new TestProvider(new TestPackage(new UObject { Name = "Actual" }), second);
        var crawler = new ObjectCrawler(provider, _ => { });

        ReadPackage(crawler, first);
        ReadPackage(crawler, second);

        var collision = Assert.Single(Issues(crawler));
        Assert.Contains($"first decoded at {first}#export/0", collision.Message);
        Assert.Contains($"repeated at {second}#export/0", collision.Message);
    }

    // Exercise the actual package loop without constructing an unrelated registry binary.
    private static void ReadPackage(ObjectCrawler crawler, string path) => typeof(ObjectCrawler)
        .GetMethod("ReadPackage", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(crawler, [path, "definition"]);

    private static List<ExtractionIssue> Issues(ObjectCrawler crawler) => (List<ExtractionIssue>)typeof(ObjectCrawler)
        .GetField("issues", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(crawler)!;

    private sealed class TestProvider : TheiaFileProvider
    {
        private readonly IPackage package;
        public TestProvider(IPackage package, params string[] additionalPaths) : base(Path.GetTempPath(), SearchOption.TopDirectoryOnly,
            new VersionContainer(EGame.GAME_ArcRaiders), StringComparer.OrdinalIgnoreCase)
        {
            this.package = package;
            MappingsContainer = new FileUsmapTypeMappingsProvider(Path.Combine(AppContext.BaseDirectory, "mappings", "ArcRaiders.usmap"));
            var file = new TestFile("PioneerGame/Content/Selected.uasset");
            var files = new Dictionary<string, GameFile>(StringComparer.OrdinalIgnoreCase) { [file.Path] = file };
            foreach (var path in additionalPaths) files.Add(path, new TestFile(path));
            Files.AddFiles(files);
        }
        public override IPackage LoadPackage(GameFile file) => package;
    }

    private sealed class TestPackage : AbstractUePackage
    {
        public TestPackage(params UObject[] exports) : base("/Game/Selected", null)
        {
            foreach (var export in exports) export.Outer = new ResolvedLoadedObject(this);
            ExportsLazy = exports.Select(export => new Lazy<UObject>(() => export)).ToArray();
        }
        public override FPackageFileSummary Summary => throw new NotSupportedException();
        public override FNameEntrySerialized[] NameMap => [];
        public override int ImportMapLength => 0;
        public override int ExportMapLength => ExportsLazy.Length;
        public override int GetExportIndex(string name, StringComparison comparisonType = StringComparison.Ordinal) => -1;
        public override ResolvedObject? ResolvePackageIndex(FPackageIndex? index) => null;
    }

    private sealed class TestFile(string path) : GameFile(path, 0)
    {
        public override bool IsEncrypted => false;
        public override CompressionMethod CompressionMethod => CompressionMethod.None;
        public override byte[] Read(FByteBulkDataHeader? header = null) => throw new NotSupportedException();
        public override FArchive CreateReader(FByteBulkDataHeader? header = null) => throw new NotSupportedException();
    }
}
