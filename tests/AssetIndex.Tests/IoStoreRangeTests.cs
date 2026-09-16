using System.IO.Compression;
using System.Text;
using CUE4Parse.UE4.Assets.Objects;
using CUE4Parse.UE4.IO;
using CUE4Parse.UE4.IO.Objects;
using CUE4Parse.UE4.Readers;
using CUE4Parse.UE4.Versions;

namespace AssetIndex.Tests;

public sealed class IoStoreRangeTests
{
    [Fact]
    public void CachedRangesMatchThePinnedUpstreamCookedFixture()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Fixtures", "IoStore", "CUE4ParseFixtures-Minimal-Zlib-Windows.utoc");
        using var reader = new IoStoreReader(path, versions: new VersionContainer(EGame.GAME_UE5_8));
        var file = Enumerable.Range(0, reader.TocResource.ChunkIds.Length)
            .Select(index => new FIoStoreEntry(reader, (uint)index))
            .Where(entry => entry.IsPackageData).MaxBy(entry => entry.Size)!;
        var blockSize = checked((int)reader.TocResource.Header.CompressionBlockSize);
        Assert.True(file.Size >= 3L * blockSize);
        var expected = file.Read();
        using var store = new PackageArchiveStore(blockSize, blockSize);
        using var archive = store.Open(file, reader.Versions, (offset, count) => PackageProvider.ReadRange(file, offset, count));
        foreach (var (offset, count) in new[] { (0, 17), (blockSize - 4, 8), (2 * blockSize - 1, blockSize + 2), (expected.Length - 13, 13), (0, 17) })
        {
            archive.Position = offset;
            Assert.Equal(expected.AsSpan(offset, count).ToArray(), archive.ReadBytes(count));
        }
        Assert.True(store.SpooledBytes < file.Size);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void RangesMatchFullExtractionAcrossBlocksAndPartitions(bool compressed)
    {
        using var fixture = new ContainerFixture(compressed);
        Assert.Equal(fixture.Payload, fixture.Entry.Read());
        fixture.ClearReads();

        Assert.Equal(fixture.Payload[..4], PackageProvider.ReadRange(fixture.Entry, 0, 4));
        Assert.Equal(1, fixture.Reads);
        Assert.Equal(fixture.Payload[17..103], PackageProvider.ReadRange(fixture.Entry, 17, 86));
        Assert.Equal(fixture.Payload[^7..], PackageProvider.ReadRange(fixture.Entry, fixture.Payload.Length - 7, 7));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CachedHeaderReadsLeaveUntouchedPayloadBlocksUnread(bool compressed)
    {
        using var fixture = new ContainerFixture(compressed);
        using var store = new PackageArchiveStore(32, 32);
        using var archive = store.Open(fixture.Entry, fixture.Reader.Versions,
            (offset, count) => PackageProvider.ReadRange(fixture.Entry, offset, count));
        Assert.Equal(0, fixture.Reads);
        Assert.Equal(fixture.Payload[..4], archive.ReadBytes(4));
        Assert.Equal(2, fixture.Reads); // Package starts inside a compression block.
        Assert.Equal(fixture.Payload[4..8], archive.ReadBytes(4));
        Assert.Equal(2, fixture.Reads);
        Assert.Equal(32, store.SpooledBytes);

        using var clone = (PackageArchive)archive.Clone();
        clone.Position = 128;
        Assert.Equal(fixture.Payload[128..133], clone.ReadBytes(5));
        var readsAfterEviction = fixture.Reads;
        archive.Position = 0;
        Assert.Equal(fixture.Payload[..8], archive.ReadBytes(8));
        Assert.Equal(readsAfterEviction, fixture.Reads);
        Assert.Equal(64, store.SpooledBytes);
        Assert.Equal(133, clone.Position);
    }

    [Fact]
    public void EmptyAndInvalidRangesNeverReadTheContainer()
    {
        using var fixture = new ContainerFixture(true);
        Assert.Empty(PackageProvider.ReadRange(fixture.Entry, 1, 0));
        Assert.Empty(PackageProvider.ReadRange(fixture.Entry, fixture.Payload.Length, 0));
        Assert.Throws<EndOfStreamException>(() => PackageProvider.ReadRange(fixture.Entry, -1, 1));
        Assert.Throws<EndOfStreamException>(() => PackageProvider.ReadRange(fixture.Entry, 0, -1));
        Assert.Throws<EndOfStreamException>(() => PackageProvider.ReadRange(fixture.Entry, fixture.Payload.Length, 1));
        Assert.Throws<EndOfStreamException>(() => PackageProvider.ReadRange(fixture.Entry, long.MaxValue, 1));
        Assert.Equal(0, fixture.Reads);
    }

    [Fact]
    public void AReaderIgnoringTheRangeFailsInsteadOfReturningTheWrongWindow()
    {
        using var fixture = new ContainerFixture(true);
        var file = new WholeChunkEntry(fixture.Reader);
        Assert.Throws<InvalidDataException>(() => PackageProvider.ReadRange(file, 1, 4));
    }

    // A serialized synthetic TOC and payload exercise the actual upstream reader.
    // This proves range/block behavior, not game mounting or Theia encryption.
    private sealed class ContainerFixture : IDisposable
    {
        private const int BlockSize = 32;
        private const int PartitionSize = 256;
        private const int PackageOffset = 11;
        private readonly List<CountingArchive> streams = [];
        public IoStoreReader Reader { get; }
        public FIoStoreEntry Entry { get; }
        public byte[] Payload { get; }
        public int Reads => streams.Sum(stream => stream.Reads);

        public ContainerFixture(bool compressed)
        {
            var data = Enumerable.Range(0, BlockSize * 8).Select(index => (byte)(index * 37)).ToArray();
            Payload = data[PackageOffset..^13];
            var blocks = data.Chunk(BlockSize).Select(bytes => Encode(bytes, compressed)).ToArray();
            using var toc = new MemoryStream();
            using (var writer = new BinaryWriter(toc, Encoding.UTF8, true))
            {
                WriteHeader(writer, blocks.Length, compressed);
                writer.Write(1UL); // Chunk ID, with ExportBundleData type.
                writer.Write((ushort)0);
                writer.Write((byte)0);
                writer.Write((byte)EIoChunkType5.ExportBundleData);
                WriteBigEndian40(writer, PackageOffset);
                WriteBigEndian40(writer, Payload.Length);
                for (var index = 0; index < blocks.Length; index++)
                {
                    writer.Write((ulong)(uint)(index * PartitionSize) | ((ulong)(uint)blocks[index].Length << 40));
                    writer.Write((uint)BlockSize | (compressed ? 1U << 24 : 0));
                }
                writer.Write(Encoding.ASCII.GetBytes("Zlib"));
                writer.Write(new byte[28]);
            }
            var versions = new VersionContainer(EGame.GAME_UE5_4);
            using var tocArchive = new FByteArchive("Fixture.utoc", toc.ToArray(), versions);
            Reader = new IoStoreReader(tocArchive, name =>
            {
                var partition = new byte[PartitionSize];
                blocks[streams.Count].CopyTo(partition, 0);
                var stream = new CountingArchive(name, partition, versions);
                streams.Add(stream);
                return stream;
            }, EIoStoreTocReadOptions.Default);
            Entry = new FIoStoreEntry(Reader, "Fixture.uasset", 0);
        }

        private static byte[] Encode(byte[] bytes, bool compressed)
        {
            if (!compressed) return bytes;
            using var output = new MemoryStream();
            using (var encoder = new ZLibStream(output, CompressionLevel.Fastest, true)) encoder.Write(bytes);
            return output.ToArray();
        }

        private static void WriteHeader(BinaryWriter writer, int blocks, bool compressed)
        {
            writer.Write(FIoStoreTocHeader.TOC_MAGIC);
            writer.Write((byte)EIoStoreTocVersion.PartitionSize);
            writer.Write((byte)0);
            writer.Write((ushort)0);
            writer.Write((uint)FIoStoreTocHeader.SIZE);
            writer.Write(1U); // One package entry.
            writer.Write((uint)blocks);
            writer.Write((uint)FIoStoreTocCompressedBlockEntry.SIZE);
            writer.Write(1U); // Zlib is declared; raw blocks use method index zero.
            writer.Write(32U); // Compression method name width.
            writer.Write((uint)BlockSize);
            writer.Write(0U); // No directory index.
            writer.Write((uint)blocks); // One block per partition.
            writer.Write(0UL); // Container ID.
            writer.Write(new byte[16]); // Encryption key GUID.
            writer.Write((byte)(compressed ? EIoContainerFlags.Compressed : EIoContainerFlags.None));
            writer.Write((byte)0); // Encryption method.
            writer.Write((ushort)0);
            writer.Write(0U); // Perfect hash seed count.
            writer.Write((ulong)PartitionSize);
            writer.Write(0U); // Overflow chunk count.
            writer.Write(new byte[44]); // Reserved fields.
            Assert.Equal(FIoStoreTocHeader.SIZE, writer.BaseStream.Position);
        }

        private static void WriteBigEndian40(BinaryWriter writer, int value)
        {
            for (var shift = 32; shift >= 0; shift -= 8) writer.Write((byte)((long)value >> shift));
        }

        public void ClearReads() { foreach (var stream in streams) stream.Reads = 0; }
        public void Dispose() => Reader.Dispose();
    }

    private sealed class CountingArchive(string name, byte[] data, VersionContainer versions) : FByteArchive(name, data, versions)
    {
        public int Reads;
        public override int ReadAt(long position, byte[] buffer, int offset, int count)
        {
            Reads++;
            return base.ReadAt(position, buffer, offset, count);
        }
    }

    private sealed class WholeChunkEntry(IoStoreReader reader) : FIoStoreEntry(reader, "Whole.uasset", 0)
    {
        public override byte[] Read(FByteBulkDataHeader? header = null) => base.Read();
    }
}
