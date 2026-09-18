using System.Buffers.Binary;
using AssetIndex.Discovery;
using CUE4Parse.FileProvider.Objects;
using CUE4Parse.UE4.Assets.Objects;
using CUE4Parse.UE4.IO;
using CUE4Parse.UE4.IO.Objects;
using CUE4Parse.UE4.Readers;
using CUE4Parse.UE4.Versions;

namespace AssetIndex.Tests;

public sealed class ImportPackageIdentityTests
{
    private static readonly FPackageId OwnerId = FPackageId.FromName("/Game/Owner");
    private static readonly FPackageId ImportedId = FPackageId.FromName("/Game/Imported");

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ExactOwningStoreEntryRetainsTheSerializedIdWithoutLoadingItsIndexedTarget(bool optional)
    {
        using var reader = Reader();
        var header = reader.ContainerHeader!;
        if (optional)
        {
            header.OptionalSegmentPackageIds = [OwnerId];
            header.OptionalSegmentStoreEntries = [Store(ImportedId)];
        }
        else
        {
            header.PackageIds = [OwnerId];
            header.StoreEntries = [Store(ImportedId)];
        }
        var target = new UnreadableEntry(reader, "Imported.uasset");
        var indexed = new Dictionary<FPackageId, GameFile> { [ImportedId] = target };

        var result = Describe(reader, 0, indexed);

        Assert.Contains($"packageId=0x{OwnerId.id:X16},store={(optional ? "optional" : "normal")},index=0", result);
        Assert.Contains($"slot=0,importedPackageId=0x{ImportedId.id:X16}", result);
        Assert.Contains("indexedTarget={file=\"Imported.uasset\",container=", result);
        Assert.Contains("source={file=\"Shared.uasset\",container=", result);
    }

    [Fact]
    public void NormalStorePrecedesOptionalAndAnAbsentIndexedIdIsExplicit()
    {
        using var reader = Reader();
        var header = reader.ContainerHeader!;
        header.PackageIds = header.OptionalSegmentPackageIds = [OwnerId];
        header.StoreEntries = [Store(ImportedId)];
        header.OptionalSegmentStoreEntries = [Store(FPackageId.FromName("/Game/Other"))];

        var result = Describe(reader);

        Assert.Contains("store=normal", result);
        Assert.Contains($"importedPackageId=0x{ImportedId.id:X16},indexedTarget=none", result);
    }

    [Fact]
    public void SameLogicalSourcePathUsesItsActualOwningContainer()
    {
        using var original = Reader();
        using var replacement = Reader();
        original.ContainerHeader!.PackageIds = replacement.ContainerHeader!.PackageIds = [OwnerId];
        original.ContainerHeader.StoreEntries = [Store(ImportedId)];
        var replacementId = FPackageId.FromName("/Game/Replacement");
        replacement.ContainerHeader.StoreEntries = [Store(replacementId)];

        Assert.Contains($"importedPackageId=0x{ImportedId.id:X16}", Describe(original));
        Assert.Contains($"importedPackageId=0x{replacementId.id:X16}", Describe(replacement));
    }

    [Fact]
    public void MissingDirectEntryDoesNotPretendFallbackImportSlotsAreSerializedIds()
    {
        using var reader = Reader();

        Assert.Equal("unavailable(no direct store entry; fallback imports not inspected)", Describe(reader));
    }

    [Theory]
    [InlineData(0U)]
    [InlineData(uint.MaxValue)]
    public void MissingImportSlotRemainsAnExplicitBoundsError(uint slot)
    {
        using var reader = Reader();
        reader.ContainerHeader!.PackageIds = [OwnerId];
        reader.ContainerHeader.StoreEntries = [Store()];

        Assert.Contains($"slot={slot},error=out-of-range(length=0)", Describe(reader, slot));
    }

    [Fact]
    public void InvalidNormalEntryDoesNotFallThroughToOptionalMetadata()
    {
        using var reader = Reader();
        var header = reader.ContainerHeader!;
        header.PackageIds = header.OptionalSegmentPackageIds = [OwnerId];
        header.OptionalSegmentStoreEntries = [Store(ImportedId)];

        Assert.Equal("unavailable(invalid direct store entry)", Describe(reader));
    }

    private static string Describe(IoStoreReader reader, uint slot = 0,
        IReadOnlyDictionary<FPackageId, GameFile>? indexed = null) =>
        ImportDiagnostics.DescribePackageIdentity(new UnreadableEntry(reader, "Shared.uasset"),
            "/Game/Owner", slot, indexed ?? new Dictionary<FPackageId, GameFile>());

    private static IoStoreReader Reader()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Fixtures", "IoStore", "CUE4ParseFixtures-Minimal-Zlib-Windows.utoc");
        var reader = new IoStoreReader(path, versions: new VersionContainer(EGame.GAME_UE5_8));
        reader.Mount(StringComparer.OrdinalIgnoreCase);
        // Parse the pinned cooked header once, as PackageProvider already did for
        // a real owner; replace only its public metadata arrays for these cases.
        var header = Assert.IsType<FIoContainerHeader>(reader.ContainerHeader);
        header.PackageIds = header.OptionalSegmentPackageIds = [];
        header.StoreEntries = header.OptionalSegmentStoreEntries = [];
        return reader;
    }

    internal static FFilePackageStoreEntry Store(params FPackageId[] imports)
    {
        var bytes = new byte[16 + imports.Length * sizeof(ulong)];
        BinaryPrimitives.WriteInt32LittleEndian(bytes, imports.Length);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(4), 16);
        for (var index = 0; index < imports.Length; index++)
            BinaryPrimitives.WriteUInt64LittleEndian(bytes.AsSpan(16 + index * sizeof(ulong)), imports[index].id);
        using var archive = new FByteArchive("StoreEntry", bytes);
        return new(archive, EIoContainerHeaderVersion.NoExportInfo);
    }

    private sealed class UnreadableEntry(IoStoreReader reader, string path) : FIoStoreEntry(reader, path, 0)
    {
        public override byte[] Read(FByteBulkDataHeader? header = null) => throw new InvalidOperationException("Diagnostics must not read packages.");
        public override FArchive CreateReader(FByteBulkDataHeader? header = null) => throw new InvalidOperationException("Diagnostics must not create archives.");
    }
}
