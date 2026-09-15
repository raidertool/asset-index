using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using CUE4Parse.UE4.Readers;
using CUE4Parse.UE4.Versions;

namespace AssetIndex;

internal sealed class PackageArchive(PackageArchiveStore store, PackageArchiveStore.Entry entry, VersionContainer versions) : FArchive(versions)
{
    public override string Name => entry.Name;
    public override long Length => entry.Size;
    public override bool CanSeek => true;
    public override long Position { get; set; }

    public override int Read(byte[] buffer, int offset, int count)
    {
        var read = ReadAt(Position, buffer, offset, count);
        Position += read;
        return read;
    }

    public override int ReadAt(long position, byte[] buffer, int offset, int count) => ReadAt(position, buffer.AsSpan(offset, count));

    public override int ReadAt(long position, Span<byte> destination)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(position);
        var length = (int)Math.Min(destination.Length, Math.Max(0, Length - position));
        store.Read(entry, Math.Min(position, Length), length).CopyTo(destination);
        return length;
    }

    public override ReadOnlySpan<byte> ReadSpan(int length)
    {
        var result = store.Read(entry, Position, length);
        Position += length;
        return result;
    }

    public override T Read<T>() => Unsafe.ReadUnaligned<T>(ref MemoryMarshal.GetReference(ReadSpan(Unsafe.SizeOf<T>())));
    public override byte[] ReadBytes(int length) => ReadSpan(length).ToArray();

    public override Task<int> ReadAtAsync(long position, byte[] buffer, int offset, int count, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(ReadAt(position, buffer, offset, count));
    }

    public override ValueTask<int> ReadAtAsync(long position, Memory<byte> memory, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult(ReadAt(position, memory.Span));
    }

    public override long Seek(long offset, SeekOrigin origin)
    {
        var position = origin switch
        {
            SeekOrigin.Begin => offset,
            SeekOrigin.Current => checked(Position + offset),
            SeekOrigin.End => checked(Length + offset),
            _ => throw new ArgumentOutOfRangeException(nameof(origin))
        };
        if (position < 0) throw new IOException("Cannot seek before the package start.");
        return Position = position;
    }

    public override object Clone() => new PackageArchive(store, entry, Versions) { Position = Position };
}
