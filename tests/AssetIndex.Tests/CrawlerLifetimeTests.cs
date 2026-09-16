using System.Runtime.CompilerServices;
using AssetIndex.Discovery;
using CUE4Parse.Compression;
using CUE4Parse.FileProvider.Objects;
using CUE4Parse.MappingsProvider.Usmap;
using CUE4Parse.UE4.Assets;
using CUE4Parse.UE4.Assets.Exports;
using CUE4Parse.UE4.Assets.Exports.Texture;
using CUE4Parse.UE4.Assets.Objects;
using CUE4Parse.UE4.Assets.Objects.Properties;
using CUE4Parse.UE4.Readers;

namespace AssetIndex.Tests;

public sealed class CrawlerLifetimeTests
{
    [Fact]
    public void DetachedCatalogAllowsCollectionAndLateReferencesStillDecodeTheExactExport()
    {
        using var provider = new ReloadingProvider();
        var (crawler, result, evidence, headers) = Crawl(provider);

        Collect();

        Assert.All(provider.Packages, package => Assert.False(package.IsAlive));
        Assert.All(provider.Objects, source => Assert.False(source.IsAlive));
        Assert.Empty(result.Issues);
        Assert.Equal(42, Assert.Single(result.Assets).Id);
        Assert.Equal("/Plugin/A.Identity", Assert.Single(result.Assets[0].Definitions).Reference.Path);
        Assert.Equal(["/Plugin/A.Icon", "/Plugin/A.Identity", "/Plugin/Z.Trigger"], evidence);
        Assert.Equal(4, headers.Count);
        var read = Assert.Single(result.Packages, package => package.Name == "/Plugin/A");
        Assert.Equal([0, 1], read.Selected);
        Assert.Equal(read.Selected, read.Decoded);
        Assert.Equal("succeeded", read.Status);
        Assert.DoesNotContain("Unused", provider.BodyReads);
        Assert.True(provider.Reads.Count(path => path == "Plugin/A.uasset") >= 2);
        GC.KeepAlive(crawler);
        GC.KeepAlive(result);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static (ObjectCrawler, DiscoveryResult, string[], List<ExportHeader>) Crawl(ReloadingProvider provider)
    {
        var evidence = new List<string>();
        var headers = new List<ExportHeader>();
        var crawler = new ObjectCrawler(provider, row => evidence.Add(row.Path), headers.Add);
        var result = crawler.Read([], (_, _) => { });
        return (crawler, result, evidence.Order(StringComparer.Ordinal).ToArray(), headers);
    }

    private static void Collect()
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
    }

    private sealed class ReloadingProvider : PackageProvider
    {
        public List<WeakReference> Packages { get; } = [];
        public List<WeakReference> Objects { get; } = [];
        public List<string> Reads { get; } = [];
        public List<string> BodyReads { get; } = [];

        public ReloadingProvider() : base(Path.GetTempPath())
        {
            MappingsContainer = new FileUsmapTypeMappingsProvider(Path.Combine(AppContext.BaseDirectory, "mappings", "ArcRaiders.usmap"));
            Files.AddFiles(new Dictionary<string, GameFile>
            {
                ["Plugin/A.uasset"] = new InputFile("Plugin/A.uasset"),
                ["Plugin/Z.uasset"] = new InputFile("Plugin/Z.uasset")
            });
        }

        protected override IPackage ReadPackage(GameFile file)
        {
            Reads.Add(file.Path);
            var package = file.Path == "Plugin/A.uasset"
                ? new CrawlerPackage(file.Path, "/Plugin/A",
                    new("Identity", "PersistenceDataAsset") { OnLoad = ObserveIdentity },
                    new("Icon", "Texture2D") { Factory = () => new UTexture2D(), OnLoad = Observe },
                    new("Unused", "Texture2D") { OnLoad = Observe })
                : new CrawlerPackage(file.Path, "/Plugin/Z", new CrawlerExport("Trigger") { OnLoad = LinkAfterCollection });
            Packages.Add(new(package));
            return package;
        }

        private void ObserveIdentity(UObject source)
        {
            source.Properties.Add(new FPropertyTag { Name = "AssetId", PropertyType = "Int64Property", Tag = new Int64Property(42) });
            Observe(source);
        }

        private void LinkAfterCollection(UObject source)
        {
            // The crawler remains active; the previous export and its owner must already be releasable.
            Collect();
            Assert.False(Assert.Single(Objects).IsAlive);
            CrawlerExport.Link(source, "/Plugin/A.Icon");
            Observe(source);
        }

        private void Observe(UObject source)
        {
            BodyReads.Add(source.Name);
            Objects.Add(new(source));
        }
    }

    private sealed class InputFile(string path) : GameFile(path, 0)
    {
        public override bool IsEncrypted => false;
        public override CompressionMethod CompressionMethod => CompressionMethod.None;
        public override byte[] Read(FByteBulkDataHeader? header = null) => throw new NotSupportedException();
        public override FArchive CreateReader(FByteBulkDataHeader? header = null) => throw new NotSupportedException();
    }
}
