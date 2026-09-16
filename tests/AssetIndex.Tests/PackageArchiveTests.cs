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
        using var store = new PackageArchiveStore(16, 8);
        using var actual = new FixtureFile("Example.uasset", bytes).Open(store);
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
        using var first = new FixtureFile("Clone.uasset", [10, 20, 30, 40]).Open(store);
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
        using var first = firstFile.Open(store);
        var borrowed = first.ReadSpan(4);
        using var second = new FixtureFile("Second.uasset", [5, 6, 7, 8]).Open(store);
        Assert.Equal(new byte[] { 5, 6, 7, 8 }, second.ReadBytes(4));
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
        using var store = new PackageArchiveStore(8, 4);
        using var original = file.Open(store);
        var bulk = new FByteBulkData(new FAssetArchive((FArchive)original.Clone(), new FixturePackage()));
        Assert.DoesNotContain(file.Requests, request => request.Position >= file.Size - payload.Length);
        using var other = new FixtureFile("Evict.uasset", new byte[file.Size]).Open(store);
        other.ReadBytes((int)file.Size);
        Assert.Equal(payload, bulk.Data);
        Assert.Contains((file.Size - payload.Length, payload.Length), file.Requests);
        original.Position = 0;
        original.ReadBytes((int)file.Size);
        Assert.Equal(6, file.Reads);
        Assert.True(store.CachedBytes <= 8);
    }

    [Fact]
    public void PhysicalVersionsOfTheSamePathHaveSeparateSpools()
    {
        using var store = new PackageArchiveStore(2);
        var oldFile = new FixtureFile("Same.uasset", [1, 2]);
        var newFile = new FixtureFile("Same.uasset", [3, 4]);
        using var oldArchive = oldFile.Open(store);
        using var newArchive = newFile.Open(store);
        Assert.Equal(new byte[] { 1, 2 }, oldArchive.ReadBytes(2));
        Assert.Equal(new byte[] { 3, 4 }, newArchive.ReadBytes(2));
        Assert.Equal(2, Directory.GetFiles(store.DirectoryPath).Length);
        Assert.Equal(4, store.SpooledBytes);
        Assert.Equal(1, oldFile.Reads);
        Assert.Equal(1, newFile.Reads);
    }

    [Fact]
    public void LargePackagesOnlyFetchRequestedPagesAndNeverReadSparseHoles()
    {
        var file = new FixtureFile("Large.uasset", Enumerable.Range(0, 32).Select(value => (byte)value).ToArray());
        using var store = new PackageArchiveStore(4);
        using var archive = file.Open(store);
        Assert.Equal(0, store.CachedBytes);
        Assert.Equal(0, store.SpooledBytes);
        archive.Position = 20;
        Assert.Equal(new byte[] { 20, 21, 22 }, archive.ReadBytes(3));
        Assert.Equal(4, store.SpooledBytes);
        archive.Position = 0;
        Assert.Equal((byte)0, archive.Read<byte>());
        Assert.Equal(4, store.CachedBytes);
        Assert.Equal(8, store.SpooledBytes);
        Assert.Equal(new (long, int)[] { (20, 4), (0, 4) }, file.Requests);
        archive.Position = 20;
        Assert.Equal((byte)20, archive.Read<byte>());
        Assert.Equal(2, file.Reads);
    }

    [Fact]
    public void ManyPackagesRespectTheCacheByteCeiling()
    {
        using var store = new PackageArchiveStore(128);
        var archives = Enumerable.Range(0, 100).Select(index =>
            new FixtureFile($"{index}.uasset", new byte[47]).Open(store)).ToArray();
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
        var archive = new FixtureFile("Gone.uasset", [1]).Open(store);
        Assert.Equal((byte)1, archive.Read<byte>());
        var path = store.DirectoryPath;
        Assert.True(Directory.Exists(path));
        store.Dispose();
        Assert.False(Directory.Exists(path));
        Assert.Equal(0, store.CachedBytes);
        Assert.Equal(0, store.SpooledBytes);
        Assert.Throws<ObjectDisposedException>(() => archive.Read<byte>());
        Assert.Throws<ObjectDisposedException>(() => new FixtureFile("Later.uasset", [2]).Open(store));
        store.Dispose();
    }

    [Fact]
    public void TruncatedSpoolsFailExplicitlyWithoutRefetching()
    {
        using var store = new PackageArchiveStore(4);
        var file = new FixtureFile("Valid.uasset", [1, 2, 3, 4]);
        using var archive = file.Open(store);
        archive.ReadBytes(4);
        archive.Position = 0;
        var spool = Directory.GetFiles(store.DirectoryPath).Single();
        using var evict = new FixtureFile("Other.uasset", [5, 6, 7, 8]).Open(store);
        evict.ReadBytes(4);
        File.WriteAllBytes(spool, [1]);
        Assert.Throws<EndOfStreamException>(() => archive.ReadBytes(4));
        Assert.Equal(0, archive.Position);
        Assert.Equal(1, file.Reads);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void LaterPageWritesCannotRestoreMissingSpoolBytesAsZeros(bool delete)
    {
        using var store = new PackageArchiveStore(4);
        var file = new FixtureFile("Damaged.uasset", [1, 2, 3, 4, 5, 6, 7, 8, 9]);
        using var archive = file.Open(store);
        Assert.Equal(new byte[] { 1, 2, 3, 4 }, archive.ReadBytes(4));
        var spool = Directory.GetFiles(store.DirectoryPath).Single();
        using var evict = new FixtureFile("Other.uasset", [10, 11, 12, 13]).Open(store);
        evict.ReadBytes(4);
        if (delete) File.Delete(spool);
        else File.WriteAllBytes(spool, []);

        archive.Position = 8;
        Assert.ThrowsAny<IOException>(() => archive.Read<byte>());
        Assert.Equal(8, archive.Position);
        Assert.Equal(8, store.SpooledBytes);
        if (delete) Assert.False(File.Exists(spool));
        else Assert.Equal(0, new FileInfo(spool).Length);
        archive.Position = 0;
        Assert.ThrowsAny<IOException>(() => archive.ReadBytes(4));
    }

    [Fact]
    public async Task InvalidBoundsAndCancelledReadsDoNotAdvancePosition()
    {
        using var store = new PackageArchiveStore(4);
        var file = new FixtureFile("Bounds.uasset", [1, 2]);
        using var archive = file.Open(store);
        Assert.Throws<EndOfStreamException>(() => archive.ReadBytes(3));
        Assert.Throws<EndOfStreamException>(() => archive.ReadBytes(-1));
        Assert.Throws<IOException>(() => archive.Seek(-1, SeekOrigin.Begin));
        Assert.Throws<ArgumentOutOfRangeException>(() => archive.ReadAt(-1, new byte[1], 0, 1));
        await Assert.ThrowsAsync<OperationCanceledException>(() => archive.ReadAtAsync(0, new byte[1], 0, 1, new CancellationToken(true)));
        Assert.Equal(0, archive.Position);
        Assert.Empty(file.Requests);
    }

    [Fact]
    public void OpeningAndEmptyReadsDoNotFetchHugePackages()
    {
        using var store = new PackageArchiveStore(8, 4);
        var file = new FixtureFile("Huge.uasset", [], (long)int.MaxValue + 100);
        using var archive = file.Open(store);
        Assert.Empty(archive.ReadBytes(0));
        Assert.Equal(0, archive.ReadAt(archive.Length + 1, new byte[1], 0, 1));
        Assert.Empty(file.Requests);
        Assert.Equal(0, store.CachedBytes);
        Assert.Equal(0, store.SpooledBytes);
        Assert.Empty(Directory.GetFiles(store.DirectoryPath));
    }

    [Fact]
    public void PagesBeyondInt32OffsetsKeepTheirFullPosition()
    {
        using var store = new PackageArchiveStore(4);
        var size = (long)int.MaxValue + 17;
        var file = new FixtureFile("Long.uasset", [], size);
        using var archive = store.Open(file, Versions, (position, length) =>
        {
            Assert.Equal(size - 4, position);
            Assert.Equal(4, length);
            return [9, 8, 7, 6];
        });
        archive.Seek(-2, SeekOrigin.End);
        Assert.Equal(new byte[] { 7, 6 }, archive.ReadBytes(2));
        Assert.Equal(4, store.SpooledBytes);
    }

    [Fact]
    public void CrossPageReadsClipTheFinalPageAndReusePreviouslyFetchedBytes()
    {
        var file = new FixtureFile("Pages.uasset", Enumerable.Range(0, 11).Select(value => (byte)value).ToArray());
        using var store = new PackageArchiveStore(4);
        using var archive = file.Open(store);
        archive.Position = 2;
        var borrowed = archive.ReadSpan(9);
        Assert.Equal(Enumerable.Range(2, 9).Select(value => (byte)value), borrowed.ToArray());
        Assert.Equal(new (long, int)[] { (0, 4), (4, 4), (8, 3) }, file.Requests);
        Assert.Equal(11, store.SpooledBytes);
        Assert.Equal(3, store.CachedBytes);
        archive.Position = 0;
        Assert.Equal((byte)0, archive.Read<byte>());
        Assert.Equal(Enumerable.Range(2, 9).Select(value => (byte)value), borrowed.ToArray());
        Assert.Equal(3, file.Reads);
    }

    [Fact]
    public void OpeningTheSamePhysicalEntryReusesItsFetchedPages()
    {
        using var store = new PackageArchiveStore(4);
        var file = new FixtureFile("Same.uasset", [1, 2, 3, 4]);
        using var first = file.Open(store);
        using var second = store.Open(file, Versions, (_, _) => throw new InvalidOperationException("Unexpected second source."));
        Assert.Equal((byte)1, first.Read<byte>());
        Assert.Equal((byte)1, second.Read<byte>());
        Assert.Equal(1, file.Reads);
        Assert.Equal(4, store.SpooledBytes);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void FailedOrShortFetchCanBeRetriedWithoutCachingPartialData(bool throws)
    {
        using var store = new PackageArchiveStore(4);
        var file = new FixtureFile("Retry.uasset", [1, 2, 3, 4]);
        var attempts = 0;
        using var archive = store.Open(file, Versions, (position, length) =>
        {
            if (++attempts > 1) return file.ReadRange(position, length);
            if (throws) throw new IOException("Interrupted fetch.");
            return [1, 2];
        });
        if (throws) Assert.Throws<IOException>(() => archive.ReadBytes(4));
        else Assert.Throws<InvalidDataException>(() => archive.ReadBytes(4));
        Assert.Equal(0, archive.Position);
        Assert.Equal(0, store.CachedBytes);
        Assert.Equal(0, store.SpooledBytes);
        Assert.Empty(Directory.GetFiles(store.DirectoryPath));
        Assert.Equal(new byte[] { 1, 2, 3, 4 }, archive.ReadBytes(4));
        Assert.Equal(2, attempts);
        Assert.Equal(4, store.SpooledBytes);
    }

    [Fact]
    public void CrossPageFailureRetainsValidPagesAndRetriesOnlyTheMissingPage()
    {
        using var store = new PackageArchiveStore(4);
        var file = new FixtureFile("Partial.uasset", [1, 2, 3, 4, 5, 6]);
        var attempts = 0;
        using var archive = store.Open(file, Versions, (position, length) =>
        {
            if (++attempts == 2) throw new IOException("Interrupted second page.");
            return file.ReadRange(position, length);
        });
        Assert.Throws<IOException>(() => archive.ReadBytes(6));
        Assert.Equal(0, archive.Position);
        Assert.Equal(4, store.SpooledBytes);
        Assert.Equal(new byte[] { 1, 2, 3, 4, 5, 6 }, archive.ReadBytes(6));
        Assert.Equal(3, attempts);
        Assert.Equal(6, store.SpooledBytes);
    }

    private sealed class FixtureFile(string path, byte[] bytes, long? declaredSize = null) : GameFile(path, declaredSize ?? bytes.LongLength)
    {
        public List<(long Position, int Length)> Requests { get; } = [];
        public int Reads => Requests.Count;
        public override bool IsEncrypted => false;
        public override CompressionMethod CompressionMethod => CompressionMethod.None;
        public PackageArchive Open(PackageArchiveStore store) => store.Open(this, Versions, ReadRange);
        public byte[] ReadRange(long position, int length)
        {
            Requests.Add((position, length));
            return bytes.AsSpan(checked((int)position), length).ToArray();
        }
        public override byte[] Read(FByteBulkDataHeader? header = null) => throw new InvalidOperationException("Whole-file reads are forbidden.");
        public override FArchive CreateReader(FByteBulkDataHeader? header = null) => throw new InvalidOperationException("Whole-file readers are forbidden.");
    }
}
