using CUE4Parse.FileProvider.Objects;
using CUE4Parse.UE4.IO.Objects;

namespace AssetIndex.Discovery;

internal sealed record PackageReadPosition(string Container, ulong Partition, ulong Offset, long LogicalOffset);

internal static class PackageReadOrder
{
    public static PackageReadPosition? Position(GameFile? file)
    {
        if (file is not FIoStoreEntry entry || entry.Offset < 0) return null;
        var reader = entry.IoStoreReader;
        var toc = reader.TocResource;
        if (toc.Header.CompressionBlockSize == 0 || toc.Header.PartitionSize == 0) return null;
        var blockIndex = (ulong)entry.Offset / toc.Header.CompressionBlockSize;
        if (blockIndex >= (ulong)toc.CompressionBlocks.Length) return null;

        // Entry offsets address decompressed data; compressed block offsets address UCAS.
        var offset = (ulong)toc.CompressionBlocks[blockIndex].Offset;
        var partition = offset / toc.Header.PartitionSize;
        if (partition >= (ulong)reader.ContainerStreams.Count) return null;
        return new(reader.Path, partition, offset % toc.Header.PartitionSize, entry.Offset);
    }
}
