using System.Reflection;
using System.Runtime.CompilerServices;
using AssetIndex.Discovery;
using CUE4Parse.FileProvider.Objects;
using CUE4Parse.UE4.IO;
using CUE4Parse.UE4.IO.Objects;
using CUE4Parse.UE4.Readers;
using CUE4Parse.UE4.VirtualFileSystem;
using CUE4Parse.UE4.Versions;

namespace AssetIndex.Tests;

public sealed class PackageReadOrderTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void PhysicalOrderPreservesAllPackagesAndRegistryReasons(bool reverse)
    {
        var names = new[] { "A", "B", "C", "Z", "Fallback" };
        var inputs = names.Select(name => new CrawlerPackage($"Plugin/{name}.uasset", $"/Plugin/{name}", new CrawlerExport(name))).ToArray();
        using var provider = new CrawlerProvider(reverse ? inputs.Reverse().ToArray() : inputs);
        // Logical order A,Z,B deliberately differs from compressed order Z,B,A.
        var first = Files("a.utoc", [8256, 32, 4106], "A", "Z", "B");
        var second = Files("b.utoc", [0], "C");
        provider.Files.AddFiles(first.Concat(second).ToDictionary(file => file.Path, file => (GameFile)file), readOrder: 2);
        var registry = new[] { Registered("A"), Registered("C") };
        var headers = new List<ExportHeader>();

        var result = new ObjectCrawler(provider, _ => { }, headers.Add).Read(
            reverse ? registry.Reverse().ToArray() : registry, (_, _) => { });

        Assert.Equal(["Plugin/Fallback.uasset", "Plugin/Z.uasset", "Plugin/B.uasset", "Plugin/A.uasset", "Plugin/C.uasset"], provider.PackageReads);
        Assert.Equal(5, result.Files.Count);
        Assert.Equal(5, headers.Count);
        Assert.Equal(5, result.Objects.Count);
        Assert.Empty(result.Issues);
        Assert.All(result.Packages, package =>
        {
            Assert.Equal("succeeded", package.Status);
            Assert.Equal([0], package.Selected);
            Assert.Equal([0], package.Decoded);
            Assert.Equal(package.Name is "/Plugin/A" or "/Plugin/C" ? "definition" : "unindexed", package.Reason);
        });
        Assert.Same(first[1], provider.PackageFiles[1]);
        Assert.Same(first[2], provider.PackageFiles[2]);
        Assert.Same(first[0], provider.PackageFiles[3]);
        Assert.Same(second[0], provider.PackageFiles[4]);
    }

    [Fact]
    public void OnlyTheMountedWinnerIsScheduledAndMissingPackagesRemainFailures()
    {
        using var provider = new CrawlerProvider(
            new CrawlerPackage("Plugin/A.uasset", "/Plugin/A", new CrawlerExport("A")),
            new CrawlerPackage("Plugin/B.uasset", "/Plugin/B", new CrawlerExport("B")));
        var old = Files("old.utoc", [0], "A")[0];
        var current = Files("current.utoc", [256, 32], "A", "B");
        provider.Files.AddFiles(new Dictionary<string, GameFile> { [old.Path] = old }, readOrder: 1);
        provider.Files.AddFiles(current.ToDictionary(file => file.Path, file => (GameFile)file), readOrder: 2);

        var result = new ObjectCrawler(provider, _ => { }, _ => { }).Read([Registered("A"), Registered("Missing")], (_, _) => { });

        Assert.Equal(["Plugin/B.uasset", "Plugin/A.uasset"], provider.PackageReads);
        Assert.Same(current[0], provider.PackageFiles[1]);
        Assert.DoesNotContain(old, provider.PackageFiles);
        Assert.Equal(2, result.Files.Count);
        Assert.Equal(3, result.Packages.Count);
        Assert.Equal("definition", Assert.Single(result.Packages, package => package.Name == "/Plugin/A").Reason);
        Assert.Equal("failed", Assert.Single(result.Packages, package => package.Path == "/Plugin/Missing").Status);
        Assert.Contains(result.Issues, issue => issue.Stage == "inventory" && issue.Path == "/Plugin/Missing");
        Assert.Contains(result.Issues, issue => issue.Stage == "package" && issue.Path == "/Plugin/Missing");
    }

    [Theory]
    [InlineData(0U, 4096UL, 3)]
    [InlineData(1024U, 0UL, 3)]
    [InlineData(1U, 4096UL, 3)]
    [InlineData(1024U, 4096UL, 0)]
    public void InvalidPhysicalMetadataUsesTheNormalLoadFallback(uint blockSize, ulong partitionSize, int streams)
    {
        var entry = Files("fixture.utoc", [0, 32], "A", "B")[1];
        var reader = entry.IoStoreReader;
        Set(reader.TocResource.Header, "CompressionBlockSize", blockSize);
        reader.TocResource.Header.PartitionSize = partitionSize;
        Set(reader, "ContainerStreams", Enumerable.Range(0, streams).Select(_ => (FArchive)new FByteArchive("fixture.ucas", [])).ToArray());

        Assert.Null(PackageReadOrder.Position(entry));
        Assert.Null(PackageReadOrder.Position(null));
    }

    private static RegisteredObject Registered(string name) =>
        new($"/Plugin/{name}.{name}", $"/Plugin/{name}", "DataAsset", new Dictionary<string, string>());

    private static FIoStoreEntry[] Files(string container, ulong[] physicalOffsets, params string[] names)
    {
        var header = (FIoStoreTocHeader)RuntimeHelpers.GetUninitializedObject(typeof(FIoStoreTocHeader));
        Set(header, "CompressionBlockSize", 1024U);
        header.PartitionSize = 4096;
        var toc = (FIoStoreTocResource)RuntimeHelpers.GetUninitializedObject(typeof(FIoStoreTocResource));
        Set(toc, "Header", header);
        Set(toc, "ChunkIds", names.Select(name => new FIoChunkId(FPackageId.FromName("/Plugin/" + name).id, 0, EIoChunkType5.ExportBundleData)).ToArray());
        Set(toc, "CompressionBlocks", physicalOffsets.Select(offset =>
        {
            using var stream = new MemoryStream();
            using (var writer = new BinaryWriter(stream, System.Text.Encoding.UTF8, true))
            { writer.Write(offset | (64UL << 40)); writer.Write(1024U); }
            using var archive = new FByteArchive("compressed block", stream.ToArray());
            return new FIoStoreTocCompressedBlockEntry(archive);
        }).ToArray());
        Set(toc, "ChunkOffsetLengths", names.Select((_, index) =>
        {
            var bytes = new byte[10];
            var offset = (ulong)index * 1024;
            for (var part = 0; part < 5; part++) bytes[4 - part] = (byte)(offset >> (part * 8));
            bytes[9] = 128;
            using var archive = new FByteArchive("chunk range", bytes);
            return archive.Read<FIoOffsetAndLength>();
        }).ToArray());
        var reader = (IoStoreReader)RuntimeHelpers.GetUninitializedObject(typeof(IoStoreReader));
        typeof(AbstractVfsReader).GetField("<Path>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(reader, container);
        reader.Versions = new VersionContainer(EGame.GAME_ArcRaiders);
        Set(reader, "TocResource", toc);
        Set(reader, "ContainerStreams", Enumerable.Range(0, 3).Select(_ => (FArchive)new FByteArchive(container, [])).ToArray());
        return names.Select((name, index) => new FIoStoreEntry(reader, $"Plugin/{name}.uasset", (uint)index)).ToArray();
    }

    private static void Set(object target, string field, object value) =>
        target.GetType().GetField(field, BindingFlags.Instance | BindingFlags.Public)!.SetValue(target, value);
}
