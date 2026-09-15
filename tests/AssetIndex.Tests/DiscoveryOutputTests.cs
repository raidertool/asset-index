using System.IO.Compression;
using System.Text.Json;
using AssetIndex.Discovery;

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
        using var provider = Provider();
        provider.BeforeLoad = _ =>
        {
            AssertInventories(); // Executed at the first package load, before any export is decoded.
            Assert.False(File.Exists(Path.Combine(output, "discovery/objects.jsonl.gz")));
        };
        using (var evidence = new JsonLinesFile<ObjectEvidence>(output, "discovery/objects.jsonl.gz"))
        {
            var crawler = new ObjectCrawler(provider, value =>
            {
                evidence.Write(value);
                throw new IOException("Deliberate crawl output failure.");
            }, _ => { });

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
        using var provider = Provider();
        provider.BeforeLoad = _ =>
        {
            clock.Advance(30);
            using var sample = JsonDocument.Parse(log.ToString());
            Assert.Equal("load-package", sample.RootElement.GetProperty("phase").GetString());
            Assert.Equal(Package, sample.RootElement.GetProperty("package").GetString());
            log.GetStringBuilder().Clear();
        };
        var crawler = new ObjectCrawler(provider, _ =>
        {
            clock.Advance(30);
            using var sample = JsonDocument.Parse(log.ToString());
            Assert.Equal("write-evidence", sample.RootElement.GetProperty("phase").GetString());
            Assert.Equal(Package, sample.RootElement.GetProperty("package").GetString());
            Assert.Equal(0, sample.RootElement.GetProperty("exportIndex").GetInt32());
        }, _ => { }, progress);

        var result = crawler.Read([Registered], WriteInventory);

        Assert.Empty(result.Issues);
        Assert.Equal(1, result.Loaded);
    }

    private static CrawlerProvider Provider() => new(new CrawlerPackage(Package, "/Plugin/Asset", new CrawlerExport("Object")));

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

}
