using System.Reflection;
using System.Runtime.CompilerServices;
using CUE4Parse.FileProvider.Objects;
using CUE4Parse.FileProvider.Vfs;
using CUE4Parse.UE4.Assets;
using CUE4Parse.UE4.Assets.Objects;
using CUE4Parse.UE4.IO;
using CUE4Parse.UE4.IO.Objects;
using CUE4Parse.UE4.Readers;
using CUE4Parse.UE4.VirtualFileSystem;
using CUE4Parse.UE4.Versions;

namespace AssetIndex.Tests;

public sealed class MountPrecedenceTests
{
    private static readonly FPackageId PackageId = FPackageId.FromName("/Game/Shared");
    private const string PackagePath = "PioneerGame/Content/Shared.uasset";

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void PackageIdAndPathUseTheHighestReadOrderRegardlessOfMountCompletion(bool reverse)
    {
        var older = new Container("older.utoc", 3, packageHash: 1);
        var current = new Container("current.utoc", 103, packageHash: 2);
        var files = Mount(reverse ? [current, older] : [older, current]);
        Assert.Same(current.Package, files[PackagePath]);
        Assert.Same(reverse ? older.Package : current.Package, files.ById[PackageId]);

        Apply(files);

        Assert.Same(current.Package, files[PackagePath]);
        Assert.Same(current.Package, files.ById[PackageId]);
        Assert.True(files.TryGetValues(PackagePath, out var versions));
        Assert.Contains(older.Package, versions);
        Assert.Contains(current.Package, versions);
    }

    [Fact]
    public void EquivalentTiesChooseTheSamePhysicalCopyInBothIndexesAndBothMountOrders()
    {
        var first = new Container("a.utoc", 3, includePayload: true);
        var second = new Container("b.utoc", 3, includePayload: true);
        var forward = Mount(first, second);
        var reverse = Mount(second, first);

        Apply(forward);
        Apply(reverse);

        Assert.Same(forward[PackagePath], reverse[PackagePath]);
        Assert.Same(forward[PackagePath], forward.ById[PackageId]);
        Assert.Same(reverse[PackagePath], reverse.ById[PackageId]);
    }

    [Fact]
    public void RealCookedContainersUseTheDefaultMetadataReaderAndParsedStoreContext()
    {
        var directory = Path.Combine(Path.GetTempPath(), "asset-index-mount-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            foreach (var name in new[] { "a", "b" })
                foreach (var extension in new[] { ".utoc", ".ucas" })
                    File.Copy(Path.Combine(AppContext.BaseDirectory, "Fixtures", "IoStore",
                        "CUE4ParseFixtures-Minimal-Zlib-Windows" + extension), Path.Combine(directory, name + extension));
            using var first = new IoStoreReader(Path.Combine(directory, "a.utoc"), versions: new VersionContainer(EGame.GAME_UE5_8));
            using var second = new IoStoreReader(Path.Combine(directory, "b.utoc"), versions: new VersionContainer(EGame.GAME_UE5_8));
            first.Mount(StringComparer.OrdinalIgnoreCase);
            second.Mount(StringComparer.OrdinalIgnoreCase);
            var files = new FileProviderDictionary();
            files.AddFiles(second.Files, second.ReadOrder, second.PackageIdIndex);
            files.AddFiles(first.Files, first.ReadOrder, first.PackageIdIndex);

            MountPrecedence.Apply(files, StringComparer.OrdinalIgnoreCase);

            Assert.NotEmpty(first.PackageIdIndex);
            foreach (var (id, file) in first.PackageIdIndex)
            {
                Assert.Same(file, files[file.Path]);
                Assert.Same(file, files.ById[id]);
            }
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void EquivalentFallbackCopiesLoadOneOwnerPerPriorityTier(bool reverse)
    {
        var older = new Container("a.utoc", 3, packageHash: 1);
        var olderCopy = new Container("b.utoc", 3, packageHash: 1);
        var current = new Container("c.utoc", 103, packageHash: 2);
        var currentCopy = new Container("d.utoc", 103, packageHash: 2);
        Container[] containers = [older, olderCopy, current, currentCopy];
        using var provider = new RecordingProvider();
        foreach (var container in reverse ? containers.Reverse() : containers)
            provider.Files.AddFiles(container.Reader.Files, container.Reader.ReadOrder, PackageFiles(container));

        provider.NormalizeFiles(reader => reader.TocResource);
        var oldPackage = provider.LoadPackage(olderCopy.Package);
        var currentPackage = provider.LoadPackage(currentCopy.Package);

        Assert.Same(oldPackage, provider.LoadPackage(older.Package));
        Assert.Same(currentPackage, provider.LoadPackage(current.Package));
        Assert.NotSame(oldPackage, currentPackage);
        Assert.Same(older.Package, provider.SourceFile(oldPackage));
        Assert.Same(current.Package, provider.SourceFile(currentPackage));
        Assert.True(provider.TryLoadPackages(PackagePath, out var versions));
        Assert.Equal(2, versions.Distinct().Count());
        Assert.Equal(new GameFile[] { older.Package, current.Package }, provider.Reads);
    }

    [Theory]
    [InlineData("hash")]
    [InlineData("zero-hash")]
    [InlineData("missing-hash")]
    [InlineData("length")]
    public void TiedPackageCopiesRequireCompleteMatchingContentEvidence(string difference)
    {
        var first = new Container("a.utoc", 3);
        var second = new Container("b.utoc", 3);
        switch (difference)
        {
            case "hash": second.Meta(0, 2); break;
            case "zero-hash": second.Meta(0, 0); break;
            case "missing-hash": Set(second.Toc, nameof(FIoStoreTocResource.ChunkMetas), null); break;
            case "length": second.Length(0, 65); break;
        }

        Assert.Throws<InvalidDataException>(() => Apply(Mount(first, second)));
    }

    [Fact]
    public void StorageCompressionFlagsDoNotOverrideMatchingUncompressedContent()
    {
        var first = new Container("a.utoc", 3);
        var second = new Container("b.utoc", 3);
        second.Meta(0, 1, FIoStoreTocEntryMetaFlags.Compressed);
        var files = Mount(second, first);

        Apply(files);

        Assert.Same(first.Package, files[PackagePath]);
        Assert.Same(first.Package, files.ById[PackageId]);
    }

    [Fact]
    public void BothUnknownHashesCannotProveEquivalence()
    {
        var first = new Container("a.utoc", 3, packageHash: 0);
        var second = new Container("b.utoc", 3, packageHash: 0);

        Assert.Throws<InvalidDataException>(() => Apply(Mount(first, second)));
    }

    [Fact]
    public void ReorderedMetadataMustStillBindToTheOriginallyMountedChunks()
    {
        var first = new Container("a.utoc", 3, includePayload: true);
        var second = new Container("b.utoc", 3, includePayload: true);
        var reread = new Container("reread.utoc", 3, includePayload: true);
        Array.Reverse(reread.Toc.ChunkIds);

        Assert.Throws<InvalidDataException>(() => MountPrecedence.Apply(Mount(first, second),
            StringComparer.OrdinalIgnoreCase, reader => ReferenceEquals(reader, second.Reader) ? reread.Toc : reader.TocResource));
    }

    [Theory]
    [InlineData("version")]
    [InlineData("export-count")]
    [InlineData("bundle-count")]
    [InlineData("imports")]
    [InlineData("missing-entry")]
    [InlineData("missing-header")]
    public void IdenticalPackageBytesDoNotMaskDifferentConsumedStoreMetadata(string difference)
    {
        var first = new Container("a.utoc", 3);
        var second = new Container("b.utoc", 3);
        var importA = FPackageId.FromName("/Game/First");
        var importB = FPackageId.FromName("/Game/Second");
        first.Header.StoreEntries[0].ImportedPackages = [importA, importB];
        second.Header.StoreEntries[0].ImportedPackages = [importA, importB];
        switch (difference)
        {
            case "version": Set(second.Header, nameof(FIoContainerHeader.Version), EIoContainerHeaderVersion.OptionalSegmentPackages); break;
            case "export-count": second.Header.StoreEntries[0].ExportCount = 1; break;
            case "bundle-count": second.Header.StoreEntries[0].ExportBundleCount = 1; break;
            case "imports": second.Header.StoreEntries[0].ImportedPackages = [importB, importA]; break;
            case "missing-entry": second.Header.StoreEntries = []; break;
            case "missing-header": Set(second.Reader, "_containerHeader", new Lazy<FIoContainerHeader?>(() => null)); break;
        }

        Assert.Throws<InvalidDataException>(() => Apply(Mount(first, second)));
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("hash")]
    [InlineData("index")]
    [InlineData("padding")]
    [InlineData("type")]
    public void EquivalentPackageBodiesRequireTheSameCompleteOwnedPayloadSet(string difference)
    {
        var first = new Container("a.utoc", 3, includePayload: true);
        var second = new Container("b.utoc", 3, includePayload: difference != "missing");
        switch (difference)
        {
            case "hash": second.Meta(1, 3); break;
            case "index": second.Toc.ChunkIds[1] = new FIoChunkId(PackageId.id, 1, EIoChunkType5.BulkData); break;
            case "padding":
                var bytes = new byte[12];
                BitConverter.GetBytes(PackageId.id).CopyTo(bytes, 0);
                bytes[10] = 1;
                bytes[11] = (byte)EIoChunkType5.BulkData;
                using (var archive = new FByteArchive("synthetic payload identity", bytes))
                    second.Toc.ChunkIds[1] = archive.Read<FIoChunkId>();
                break;
            case "type": second.Toc.ChunkIds[1] = new FIoChunkId(PackageId.id, 0, EIoChunkType5.OptionalBulkData); break;
        }

        Assert.Throws<InvalidDataException>(() => Apply(Mount(first, second)));
    }

    [Fact]
    public void UniqueHighPriorityCopyDoesNotHideConflictingLowerPriorityFallbacks()
    {
        var first = new Container("a.utoc", 3, packageHash: 1);
        var second = new Container("b.utoc", 3, packageHash: 2);
        var current = new Container("current.utoc", 103, packageHash: 3);

        Assert.Throws<InvalidDataException>(() => Apply(Mount(first, second, current)));
    }

    [Fact]
    public void AUniquePackageWithoutItsOwnStoreEntryCannotUseGlobalHeaderFallback()
    {
        var only = new Container("only.utoc", 103);
        only.Header.PackageIds = [];
        only.Header.StoreEntries = [];

        Assert.Throws<InvalidDataException>(() => Apply(Mount(only)));
    }

    [Fact]
    public void PackageIdIndexCannotRetainAnUnrepresentedPhysicalEntry()
    {
        var hidden = new Container("hidden.utoc", 3);
        var visible = new Container("visible.utoc", 3);
        var visibleId = FPackageId.FromName("/Game/Different");
        visible.Toc.ChunkIds[0] = new FIoChunkId(visibleId.id, 0, EIoChunkType5.ExportBundleData);
        visible.Header.PackageIds = [visibleId];
        var files = new FileProviderDictionary();
        files.AddFiles(visible.Reader.Files, visible.Reader.ReadOrder, new Dictionary<FPackageId, GameFile>
        {
            [PackageId] = hidden.Package,
            [visibleId] = visible.Package
        });

        Assert.Throws<InvalidDataException>(() => Apply(files));
    }

    [Fact]
    public void OnePackageIdCannotSelectDifferentLogicalPaths()
    {
        var first = new Container("a.utoc", 3);
        var second = new Container("b.utoc", 103, pathOverride: "PioneerGame/Content/Other.uasset");

        Assert.Throws<InvalidDataException>(() => Apply(Mount(first, second)));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void OptionalOrNonzeroIndexPackageCannotReplaceTheNormalPackageId(bool optional)
    {
        var normal = new Container("normal.utoc", 3);
        var extra = new Container("extra.utoc", 103,
            pathOverride: optional ? "PioneerGame/Content/Shared.o.uasset" : "PioneerGame/Content/SharedSegment.uasset",
            chunkIndex: optional ? (ushort)0 : (ushort)1);
        if (optional)
        {
            extra.Header.OptionalSegmentPackageIds = extra.Header.PackageIds;
            extra.Header.OptionalSegmentStoreEntries = extra.Header.StoreEntries;
            extra.Header.PackageIds = [];
            extra.Header.StoreEntries = [];
        }
        var files = Mount(normal, extra);

        Apply(files);

        Assert.Same(normal.Package, files.ById[PackageId]);
        Assert.Same(normal.Package, files[normal.Package.Path]);
        Assert.Same(extra.Package, files[extra.Package.Path]);
    }

    [Theory]
    [InlineData(true, true)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(false, false)]
    public void PackagePayloadSelectionPrefersItsOwnerBeforeGlobalFallback(bool ownsBulk, bool ownsOptional)
    {
        var owner = new Container("owner.utoc", 3, includePayload: ownsBulk);
        var ownOptional = ownsOptional ? owner.AddOptional() : null;
        var newer = new Container("newer.utoc", 103, includePayload: true);
        var newerOptional = newer.AddOptional();
        var files = Mount(owner, newer);
        var bulkPath = "PioneerGame/Content/Shared.ubulk";
        Assert.Same(newer.Reader.Files[bulkPath], files[bulkPath]);
        Assert.Same(newerOptional, files[newerOptional.Path]);

        // This is the actual boundary called by PackageProvider.ReadPackage;
        // no claim is made about parsing an optional-payload package body here.
        var selected = MountPrecedence.ResolvePayloads(owner.Package, files);

        Assert.Same(ownsBulk ? owner.Reader.Files[bulkPath] : newer.Reader.Files[bulkPath], selected.Bulk);
        Assert.Same(ownOptional ?? newerOptional, selected.Optional);
    }

    [Fact]
    public void MissingPackagePayloadsRemainAbsent()
    {
        var owner = new Container("owner.utoc", 3);

        var selected = MountPrecedence.ResolvePayloads(owner.Package, Mount(owner));

        Assert.Null(selected.Bulk);
        Assert.Null(selected.Optional);
    }

    [Fact]
    public void DecodedPackageNameMustMatchItsPhysicalChunkIdentity()
    {
        var owner = new Container("owner.utoc", 3);

        MountPrecedence.RequirePackageIdentity(owner.Package, "/Game/Shared");
    }

    [Theory]
    [InlineData("/Game/Different")]
    [InlineData("/Other/Shared")]
    public void ASharedLeafNameCannotSubstituteForTheDecodedFullPackageIdentity(string decodedName)
    {
        var owner = new Container("owner.utoc", 3);

        Assert.Throws<InvalidDataException>(() => MountPrecedence.RequirePackageIdentity(owner.Package, decodedName));
    }

    private static FileProviderDictionary Mount(params Container[] containers)
    {
        var files = new FileProviderDictionary();
        foreach (var container in containers)
            files.AddFiles(container.Reader.Files, container.Reader.ReadOrder, PackageFiles(container));
        return files;
    }

    // IoStoreReader.MountTo passes this explicit index; the generic AddFiles
    // fallback does not exclude nonzero package chunk indexes.
    private static Dictionary<FPackageId, GameFile> PackageFiles(Container container) =>
        container.Reader.Files.Values.OfType<FIoStoreEntry>()
            .Where(file => file.IsPackageData && !file.IsOptionalPackage && file.ChunkId._chunkIndex == 0)
            .ToDictionary(file => file.ChunkId.AsPackageId(), file => (GameFile)file);

    private static void Apply(FileProviderDictionary files) =>
        MountPrecedence.Apply(files, StringComparer.OrdinalIgnoreCase, reader => reader.TocResource);

    // These fixtures contain parsed directory/TOC/header evidence, not package
    // payloads. Any attempted package-body read fails immediately.
    private sealed class Container
    {
        public IoStoreReader Reader { get; }
        public FIoStoreTocResource Toc { get; }
        public FIoContainerHeader Header { get; }
        public FIoStoreEntry Package { get; }

        public Container(string path, long order, byte packageHash = 1, bool includePayload = false,
            string? pathOverride = null, ushort chunkIndex = 0)
        {
            var count = includePayload ? 2 : 1;
            Toc = (FIoStoreTocResource)RuntimeHelpers.GetUninitializedObject(typeof(FIoStoreTocResource));
            Set(Toc, nameof(FIoStoreTocResource.Header), RuntimeHelpers.GetUninitializedObject(typeof(FIoStoreTocHeader)));
            Set(Toc, nameof(FIoStoreTocResource.ChunkIds), includePayload
                ? new[] { new FIoChunkId(PackageId.id, chunkIndex, EIoChunkType5.ExportBundleData), new FIoChunkId(PackageId.id, 0, EIoChunkType5.BulkData) }
                : new[] { new FIoChunkId(PackageId.id, chunkIndex, EIoChunkType5.ExportBundleData) });
            Set(Toc, nameof(FIoStoreTocResource.ChunkOffsetLengths), new FIoOffsetAndLength[count]);
            Set(Toc, nameof(FIoStoreTocResource.ChunkMetas), new FIoStoreTocEntryMeta[count]);
            for (var index = 0; index < count; index++) { Length(index, 64); Meta(index, packageHash); }
            Header = (FIoContainerHeader)RuntimeHelpers.GetUninitializedObject(typeof(FIoContainerHeader));
            Set(Header, nameof(FIoContainerHeader.Version), EIoContainerHeaderVersion.NoExportInfo);
            Header.PackageIds = [PackageId];
            Header.StoreEntries = [ImportPackageIdentityTests.Store()];
            Header.OptionalSegmentPackageIds = [];
            Header.OptionalSegmentStoreEntries = [];
            Reader = (IoStoreReader)RuntimeHelpers.GetUninitializedObject(typeof(IoStoreReader));
            Set(typeof(AbstractVfsReader), Reader, "<Path>k__BackingField", path);
            Set(typeof(AbstractVfsReader), Reader, "<Name>k__BackingField", path);
            Set(typeof(AbstractVfsReader), Reader, "<ReadOrder>k__BackingField", order);
            Reader.Versions = new VersionContainer(EGame.GAME_ArcRaiders);
            Set(Reader, nameof(IoStoreReader.TocResource), Toc);
            Set(Reader, "_containerHeader", new Lazy<FIoContainerHeader?>(() => Header));
            Package = new NoPayloadEntry(Reader, pathOverride ?? PackagePath, 0);
            var entries = new Dictionary<string, GameFile>(StringComparer.OrdinalIgnoreCase) { [Package.Path] = Package };
            if (includePayload) entries.Add("PioneerGame/Content/Shared.ubulk", new NoPayloadEntry(Reader, "PioneerGame/Content/Shared.ubulk", 1));
            Set(typeof(AbstractVfsReader), Reader, "<Files>k__BackingField", entries);
        }

        public void Meta(int index, byte hash, FIoStoreTocEntryMetaFlags flags = FIoStoreTocEntryMetaFlags.None)
        {
            var bytes = new byte[24];
            bytes.AsSpan(0, 20).Fill(hash);
            bytes[20] = (byte)flags;
            using var archive = new FByteArchive("synthetic chunk metadata", bytes);
            Toc.ChunkMetas![index] = new FIoStoreTocEntryMeta(archive, true);
        }

        public void Length(int index, byte length)
        {
            var bytes = new byte[10];
            bytes[9] = length;
            using var archive = new FByteArchive("synthetic chunk range", bytes);
            Toc.ChunkOffsetLengths[index] = archive.Read<FIoOffsetAndLength>();
        }

        public FIoStoreEntry AddOptional()
        {
            var index = Toc.ChunkIds.Length;
            Set(Toc, nameof(FIoStoreTocResource.ChunkIds), Toc.ChunkIds
                .Append(new FIoChunkId(PackageId.id, 0, EIoChunkType5.OptionalBulkData)).ToArray());
            Set(Toc, nameof(FIoStoreTocResource.ChunkOffsetLengths), Toc.ChunkOffsetLengths.Append(Toc.ChunkOffsetLengths[0]).ToArray());
            var metadata = Toc.ChunkMetas!;
            Set(Toc, nameof(FIoStoreTocResource.ChunkMetas), metadata.Append(metadata[0]).ToArray());
            var file = new NoPayloadEntry(Reader, "PioneerGame/Content/Shared.uptnl", (uint)index);
            ((Dictionary<string, GameFile>)Reader.Files).Add(file.Path, file);
            return file;
        }
    }

    private sealed class NoPayloadEntry(IoStoreReader reader, string path, uint index) : FIoStoreEntry(reader, path, index)
    {
        public override byte[] Read(FByteBulkDataHeader? header = null) => throw new InvalidOperationException("Mount selection must not read package bodies.");
        public override FArchive CreateReader(FByteBulkDataHeader? header = null) => throw new InvalidOperationException("Mount selection must not create package archives.");
    }

    private sealed class RecordingProvider() : PackageProvider(Path.GetTempPath())
    {
        public List<GameFile> Reads { get; } = [];
        protected override IPackage ReadPackage(GameFile file)
        {
            Reads.Add(file);
            return new CrawlerPackage(file.Path, "/Game/Shared", new CrawlerExport("Shared"));
        }
    }

    private static void Set(object target, string name, object? value) => Set(target.GetType(), target, name, value);
    private static void Set(Type type, object target, string name, object? value) =>
        type.GetField(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(target, value);
}
