using CUE4Parse.Compression;
using CUE4Parse.FileProvider.Objects;
using CUE4Parse.UE4.Assets.Objects;
using CUE4Parse.UE4.Assets.Readers;
using CUE4Parse.UE4.Readers;
using CUE4Parse.UE4.Versions;

namespace AssetIndex.Tests;

public sealed class PackageArchiveTests
{
    private static readonly VersionContainer Versions = new(EGame.GAME_UE4_27);

    [Fact]
    public void ReadsAndSeeksMatchTheUpstreamByteArchive()
    {
        var bytes = Enumerable.Range(0, 251).Select(index => (byte)index).ToArray();
        using var store = new PackageArchiveStore(512);
        using var actual = store.Open(new FixtureFile("Example.uasset", bytes), Versions);
        using var expected = new FByteArchive("Example.uasset", bytes, Versions);
        Assert.Equal(expected.Read<uint>(), actual.Read<uint>());
        Assert.Equal(expected.ReadSpan(9).ToArray(), actual.ReadSpan(9).ToArray());
        Assert.Equal(expected.Seek(3, SeekOrigin.Current), actual.Seek(3, SeekOrigin.Current));
        Assert.Equal(expected.ReadArray<int>(4), actual.ReadArray<int>(4));
        Assert.Equal(expected.Seek(-2, SeekOrigin.End), actual.Seek(-2, SeekOrigin.End));
        var actualBuffer = new byte[5];
        var expectedBuffer = new byte[5];
        Assert.Equal(expected.Read(expectedBuffer, 1, 4), actual.Read(actualBuffer, 1, 4));
        Assert.Equal(expectedBuffer, actualBuffer);
        Assert.Equal(expected.Position, actual.Position);
        Assert.Equal(0, actual.Read(actualBuffer, 0, 1));
        Assert.Throws<EndOfStreamException>(() => actual.ReadBytes(1));
    }

    [Fact]
    public async Task ClonesAndRandomAccessKeepIndependentPositions()
    {
        using var store = new PackageArchiveStore(16);
        using var first = store.Open(new FixtureFile("Clone.uasset", [10, 20, 30, 40]), Versions);
        first.Position = 1;
        using var clone = (PackageArchive)first.Clone();
        Assert.Equal((byte)20, clone.Read<byte>());
        Assert.Equal(1, first.Position);
        var buffer = new byte[2];
        Assert.Equal(2, await clone.ReadAtAsync(0, buffer.AsMemory()));
        Assert.Equal(new byte[] { 10, 20 }, buffer);
        Assert.Equal(2, clone.Position);
        Assert.Equal(2, await clone.ReadAtAsync(2, buffer, 0, 2));
        Assert.Equal(new byte[] { 30, 40 }, buffer);
        Assert.Equal(2, clone.Position);
    }

    [Fact]
    public void EvictionPreservesBorrowedSpansAndDoesNotRefetchGameFiles()
    {
        var firstFile = new FixtureFile("First.uasset", [1, 2, 3, 4]);
        using var store = new PackageArchiveStore(4);
        using var first = store.Open(firstFile, Versions);
        var borrowed = first.ReadSpan(4);
        using var second = store.Open(new FixtureFile("Second.uasset", [5, 6, 7, 8]), Versions);
        Assert.Equal(4, store.CachedBytes);
        Assert.Equal(new byte[] { 1, 2, 3, 4 }, borrowed.ToArray());
        first.Position = 0;
        Assert.Equal(new byte[] { 1, 2, 3, 4 }, first.ReadBytes(4));
        Assert.Equal(1, firstFile.Reads);
        Assert.Equal(4, store.CachedBytes);
    }

    [Fact]
    public void RealCueBulkDataCanReadLazilyAfterItsArchiveWasEvicted()
    {
        byte[] payload = [41, 42, 43, 44];
        using var encoded = new MemoryStream();
        using (var writer = new BinaryWriter(encoded, System.Text.Encoding.UTF8, true))
        {
            writer.Write((uint)(EBulkDataFlags.BULKDATA_ForceInlinePayload | EBulkDataFlags.BULKDATA_NoOffsetFixUp));
            writer.Write(payload.Length);
            writer.Write((uint)payload.Length);
            writer.Write(0L);
            writer.Write(payload);
        }
        var file = new FixtureFile("Lazy.uasset", encoded.ToArray());
        using var store = new PackageArchiveStore(file.Size);
        using var original = store.Open(file, Versions);
        var bulk = new FByteBulkData(new FAssetArchive((FArchive)original.Clone(), new FixturePackage()));
        using var other = store.Open(new FixtureFile("Evict.uasset", new byte[file.Size]), Versions);
        Assert.Equal(payload, bulk.Data);
        Assert.Equal(1, file.Reads);
        Assert.True(store.CachedBytes <= file.Size);
    }

    [Fact]
    public void PhysicalVersionsOfTheSamePathHaveSeparateSpools()
    {
        using var store = new PackageArchiveStore(2);
        var oldFile = new FixtureFile("Same.uasset", [1, 2]);
        var newFile = new FixtureFile("Same.uasset", [3, 4]);
        using var oldArchive = store.Open(oldFile, Versions);
        using var newArchive = store.Open(newFile, Versions);
        Assert.Equal(new byte[] { 1, 2 }, oldArchive.ReadBytes(2));
        Assert.Equal(new byte[] { 3, 4 }, newArchive.ReadBytes(2));
        Assert.Equal(2, Directory.GetFiles(store.DirectoryPath).Length);
        Assert.Equal(4, store.SpooledBytes);
        Assert.Equal(1, oldFile.Reads);
        Assert.Equal(1, newFile.Reads);
    }

    [Fact]
    public void OversizedPackagesUseSpoolRangesWithoutEnteringTheCache()
    {
        var file = new FixtureFile("Large.uasset", Enumerable.Range(0, 32).Select(value => (byte)value).ToArray());
        using var store = new PackageArchiveStore(4);
        using var archive = store.Open(file, Versions);
        Assert.Equal(0, store.CachedBytes);
        Assert.Equal(32, store.SpooledBytes);
        archive.Position = 20;
        Assert.Equal(new byte[] { 20, 21, 22 }, archive.ReadBytes(3));
        archive.Position = 0;
        Assert.Equal((byte)0, archive.Read<byte>());
        Assert.Equal(0, store.CachedBytes);
        Assert.Equal(1, file.Reads);
    }

    [Fact]
    public void ManyPackagesRespectTheCacheByteCeiling()
    {
        using var store = new PackageArchiveStore(128);
        var archives = Enumerable.Range(0, 100).Select(index =>
            store.Open(new FixtureFile($"{index}.uasset", new byte[47]), Versions)).ToArray();
        foreach (var archive in archives)
        {
            Assert.Equal(new byte[47], archive.ReadBytes(47));
            Assert.InRange(store.CachedBytes, 0, 128);
        }
    }

    [Fact]
    public void DisposalRemovesPrivateSpoolAndRejectsLaterLazyReads()
    {
        var store = new PackageArchiveStore(16);
        var archive = store.Open(new FixtureFile("Gone.uasset", [1]), Versions);
        var path = store.DirectoryPath;
        Assert.True(Directory.Exists(path));
        store.Dispose();
        Assert.False(Directory.Exists(path));
        Assert.Equal(0, store.CachedBytes);
        Assert.Equal(0, store.SpooledBytes);
        Assert.Throws<ObjectDisposedException>(() => archive.Read<byte>());
        Assert.Throws<ObjectDisposedException>(() => store.Open(new FixtureFile("Later.uasset", [2]), Versions));
        store.Dispose();
    }

    [Fact]
    public void ShortSourceReadsAndTruncatedSpoolsFailExplicitly()
    {
        using var store = new PackageArchiveStore(4);
        Assert.Throws<InvalidDataException>(() => store.Open(new FixtureFile("Short.uasset", [1, 2], 3), Versions));
        using var archive = store.Open(new FixtureFile("Valid.uasset", [1, 2, 3, 4]), Versions);
        var spool = Directory.GetFiles(store.DirectoryPath).Single();
        using var evict = store.Open(new FixtureFile("Other.uasset", [5, 6, 7, 8]), Versions);
        File.WriteAllBytes(spool, [1]);
        Assert.Throws<EndOfStreamException>(() => archive.ReadBytes(4));
    }

    [Fact]
    public async Task InvalidBoundsAndCancelledReadsDoNotAdvancePosition()
    {
        using var store = new PackageArchiveStore(4);
        using var archive = store.Open(new FixtureFile("Bounds.uasset", [1, 2]), Versions);
        Assert.Throws<EndOfStreamException>(() => archive.ReadBytes(3));
        Assert.Throws<EndOfStreamException>(() => archive.ReadBytes(-1));
        Assert.Throws<IOException>(() => archive.Seek(-1, SeekOrigin.Begin));
        Assert.Throws<ArgumentOutOfRangeException>(() => archive.ReadAt(-1, new byte[1], 0, 1));
        await Assert.ThrowsAsync<OperationCanceledException>(() => archive.ReadAtAsync(0, new byte[1], 0, 1, new CancellationToken(true)));
        Assert.Equal(0, archive.Position);
    }

    private sealed class FixtureFile(string path, byte[] bytes, long? declaredSize = null) : GameFile(path, declaredSize ?? bytes.LongLength)
    {
        public int Reads;
        public override bool IsEncrypted => false;
        public override CompressionMethod CompressionMethod => CompressionMethod.None;
        public override byte[] Read(FByteBulkDataHeader? header = null) { Reads++; return bytes.ToArray(); }
        public override FArchive CreateReader(FByteBulkDataHeader? header = null) => new FByteArchive(Path, Read(), Versions);
    }
}
