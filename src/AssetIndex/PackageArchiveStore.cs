using CUE4Parse.FileProvider.Objects;
using CUE4Parse.UE4.Versions;

namespace AssetIndex;

// Package objects and lazy bulk readers may outlive the cached package bytes.
// Fetch and spool only the pages they read, retaining a bounded subset in RAM.
internal sealed class PackageArchiveStore(long capacity = 256L * 1024 * 1024, int pageSize = 64 * 1024) : IDisposable
{
    private readonly long capacity = capacity > 0 ? capacity : throw new ArgumentOutOfRangeException(nameof(capacity));
    private readonly int pageSize = pageSize > 0 ? (int)Math.Min(capacity, pageSize) : throw new ArgumentOutOfRangeException(nameof(pageSize));
    private readonly object gate = new();
    private readonly Dictionary<GameFile, Entry> entries = new(ReferenceEqualityComparer.Instance);
    private readonly LinkedList<Page> recent = new();
    private long cachedBytes;
    private long spooledBytes;
    private bool disposed;
    internal string DirectoryPath { get; } = Directory.CreateTempSubdirectory("asset-index-packages-").FullName;
    public long CachedBytes { get { lock (gate) return cachedBytes; } }
    public long SpooledBytes { get { lock (gate) return spooledBytes; } }

    public PackageArchive Open(GameFile file, VersionContainer versions, Func<long, int, byte[]> readRange)
    {
        lock (gate)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            ArgumentNullException.ThrowIfNull(readRange);
            if (!entries.TryGetValue(file, out var entry))
            {
                ArgumentOutOfRangeException.ThrowIfNegative(file.Size);
                entry = new(file.Path, Path.Combine(DirectoryPath, entries.Count.ToString()), file.Size, readRange);
                entries.Add(file, entry);
            }
            return new(this, entry, versions);
        }
    }

    internal ReadOnlySpan<byte> Read(Entry entry, long position, int length)
    {
        lock (gate)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            if (position < 0 || length < 0 || position > entry.Size - length)
                throw new EndOfStreamException($"Read exceeds package bounds: {entry.Name}.");
            if (length == 0) return ReadOnlySpan<byte>.Empty;
            var first = ReadPage(entry, position);
            var offset = (int)(position % pageSize);
            // The returned span keeps this array alive even if another read evicts it.
            if (length <= first.Length - offset) return first.AsSpan(offset, length);

            var result = new byte[length];
            var copied = 0;
            while (copied < length)
            {
                var bytes = copied == 0 ? first : ReadPage(entry, position + copied);
                var count = Math.Min(length - copied, bytes.Length - offset);
                bytes.AsSpan(offset, count).CopyTo(result.AsSpan(copied));
                copied += count;
                offset = 0;
            }
            return result;
        }
    }

    private byte[] ReadPage(Entry entry, long position)
    {
        var start = position - position % pageSize;
        if (!entry.Pages.TryGetValue(start, out var page))
        {
            var size = (int)Math.Min(pageSize, entry.Size - start);
            var bytes = entry.ReadRange(start, size);
            if (bytes is null || bytes.Length != size)
                throw new InvalidDataException($"Package range byte count differs at {entry.Name}:{start}.");
            using (var stream = File.Open(entry.File, entry.WrittenLength == 0 ? FileMode.OpenOrCreate : FileMode.Open, FileAccess.Write))
            {
                if (stream.Length < entry.WrittenLength)
                    throw new EndOfStreamException($"Package spool was truncated: {entry.Name}.");
                stream.Position = start;
                stream.Write(bytes);
            }
            entry.WrittenLength = Math.Max(entry.WrittenLength, start + size);
            page = new(size);
            entry.Pages.Add(start, page);
            spooledBytes += size;
            Cache(page, bytes);
        }
        else if (page.Bytes is null)
        {
            var bytes = new byte[page.Size];
            using var stream = File.OpenRead(entry.File);
            stream.Position = start;
            stream.ReadExactly(bytes);
            Cache(page, bytes);
        }
        recent.Remove(page.Node!);
        recent.AddFirst(page.Node!);
        return page.Bytes!;
    }

    private void Cache(Page page, byte[] bytes)
    {
        while (cachedBytes > capacity - bytes.LongLength)
        {
            var oldest = recent.Last!.Value;
            cachedBytes -= oldest.Bytes!.LongLength;
            oldest.Bytes = null;
            oldest.Node = null;
            recent.RemoveLast();
        }
        page.Bytes = bytes;
        page.Node = recent.AddFirst(page);
        cachedBytes += bytes.LongLength;
    }

    public void Dispose()
    {
        lock (gate)
        {
            if (disposed) return;
            disposed = true;
            foreach (var page in recent) { page.Bytes = null; page.Node = null; }
            recent.Clear();
            entries.Clear();
            cachedBytes = 0;
            spooledBytes = 0;
            Directory.Delete(DirectoryPath, true);
        }
    }

    internal sealed class Entry(string name, string file, long size, Func<long, int, byte[]> readRange)
    {
        public string Name { get; } = name;
        public string File { get; } = file;
        public long Size { get; } = size;
        public Func<long, int, byte[]> ReadRange { get; } = readRange;
        public long WrittenLength;
        // Spool holes are never readable until a complete fetched page is recorded.
        public Dictionary<long, Page> Pages { get; } = [];
    }

    internal sealed class Page(int size)
    {
        public int Size { get; } = size;
        public byte[]? Bytes;
        public LinkedListNode<Page>? Node;
    }
}
