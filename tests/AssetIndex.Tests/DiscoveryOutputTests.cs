using System.IO.Compression;
using System.Text.Json;
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

public sealed class DiscoveryOutputTests : IDisposable
{
    private readonly string output = Path.Combine(Path.GetTempPath(), "discovery-output-" + Guid.NewGuid().ToString("N"));
    private const string Package = "Plugin/Asset.uasset";
    private static readonly RegisteredObject Registered = new("/Plugin/Asset.Object", "/Plugin/Asset", "DataAsset",
        new Dictionary<string, string>());

    [Fact]
    public void InventoriesSurviveACrawlFailureAndObjectEvidenceIsNeverFinalized()
    {
        using var provider = new TestProvider(() =>
        {
            AssertInventories(); // Executed at the first package load, before any export is decoded.
            Assert.False(File.Exists(Path.Combine(output, "discovery/objects.jsonl.gz")));
        });
        using (var evidence = new EvidenceFile(output))
        {
            var crawler = new ObjectCrawler(provider, value =>
            {
                evidence.Write(value);
                throw new IOException("Deliberate crawl output failure.");
            });

            var error = Assert.Throws<IOException>(() => crawler.Read([Registered], WriteInventory));

            Assert.Equal("Deliberate crawl output failure.", error.Message);
            Assert.Single(Directory.EnumerateFiles(Path.Combine(output, "discovery"), "objects.jsonl.gz.*.tmp"));
            Assert.False(File.Exists(Path.Combine(output, "discovery/objects.jsonl.gz")));
        }

        AssertInventories();
        Assert.Empty(Directory.EnumerateFiles(Path.Combine(output, "discovery"), "*.tmp"));
        Assert.False(File.Exists(Path.Combine(output, "coverage.json")));
    }

    [Fact]
    public void ProgressIdentifiesTheLoadAndWriteThatAreActuallyExecuting()
    {
        using var log = new StringWriter();
        var clock = new ExtractionProgressTests.ManualTimeProvider();
        using var progress = new ExtractionProgress(log, clock);
        using var provider = new TestProvider(() =>
        {
            clock.Advance(30);
            using var sample = JsonDocument.Parse(log.ToString());
            Assert.Equal("load-package", sample.RootElement.GetProperty("phase").GetString());
            Assert.Equal(Package, sample.RootElement.GetProperty("package").GetString());
            log.GetStringBuilder().Clear();
        });
        var crawler = new ObjectCrawler(provider, _ =>
        {
            clock.Advance(30);
            using var sample = JsonDocument.Parse(log.ToString());
            Assert.Equal("write-evidence", sample.RootElement.GetProperty("phase").GetString());
            Assert.Equal(Package, sample.RootElement.GetProperty("package").GetString());
            Assert.Equal(0, sample.RootElement.GetProperty("exportIndex").GetInt32());
        }, progress);

        var result = crawler.Read([Registered], WriteInventory);

        Assert.Empty(result.Issues);
        Assert.Equal(1, result.Loaded);
    }

    private void WriteInventory(IReadOnlyList<RegisteredObject> registry, IReadOnlyList<PackageFile> files)
    {
        Snapshot.WriteLines(output, "discovery/files.jsonl.gz", files);
        Snapshot.WriteLines(output, "discovery/registry.jsonl.gz", registry);
    }

    private void AssertInventories()
    {
        using var registry = ReadRow("registry");
        Assert.Equal(Registered.Path, registry.RootElement.GetProperty("path").GetString());
        using var files = ReadRow("files");
        Assert.Equal(Package, files.RootElement.GetProperty("path").GetString());
        Assert.Equal("/Plugin/Asset", files.RootElement.GetProperty("registryPackages")[0].GetString());
    }

    private JsonDocument ReadRow(string name)
    {
        using var stream = new GZipStream(File.OpenRead(Path.Combine(output, "discovery", name + ".jsonl.gz")), CompressionMode.Decompress);
        return JsonDocument.Parse(stream);
    }

    public void Dispose()
    {
        if (Directory.Exists(output)) Directory.Delete(output, recursive: true);
    }

    private sealed class TestProvider : TheiaFileProvider
    {
        private readonly Action loading;

        public TestProvider(Action loading) : base(Path.GetTempPath(), SearchOption.TopDirectoryOnly,
            new VersionContainer(EGame.GAME_ArcRaiders), StringComparer.OrdinalIgnoreCase)
        {
            this.loading = loading;
            MappingsContainer = new FileUsmapTypeMappingsProvider(Path.Combine(AppContext.BaseDirectory, "mappings", "ArcRaiders.usmap"));
            Files.AddFiles(new Dictionary<string, GameFile> { [Package] = new TestFile() });
        }

        public override IPackage LoadPackage(GameFile file)
        {
            loading();
            return new TestPackage();
        }
    }

    private sealed class TestPackage : AbstractUePackage
    {
        public TestPackage() : base("/Plugin/Asset", null)
        {
            var value = new UObject { Name = "Object", Outer = new ResolvedLoadedObject(this) };
            ExportsLazy = [new(() => value)];
        }
        public override FPackageFileSummary Summary => throw new NotSupportedException();
        public override FNameEntrySerialized[] NameMap => [];
        public override int ImportMapLength => 0;
        public override int ExportMapLength => ExportsLazy.Length;
        public override int GetExportIndex(string name, StringComparison comparisonType = StringComparison.Ordinal) => -1;
        public override ResolvedObject? ResolvePackageIndex(FPackageIndex? index) => null;
    }

    private sealed class TestFile() : GameFile(Package, 0)
    {
        public override bool IsEncrypted => false;
        public override CompressionMethod CompressionMethod => CompressionMethod.None;
        public override byte[] Read(FByteBulkDataHeader? header = null) => throw new NotSupportedException();
        public override FArchive CreateReader(FByteBulkDataHeader? header = null) => throw new NotSupportedException();
    }
}
