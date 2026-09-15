using CUE4Parse.FileProvider.Objects;
using CUE4Parse.UE4.Versions;

namespace AssetIndex;

// Package objects and lazy bulk readers may outlive the cached package bytes.
// The private spool preserves those bytes without retaining every archive in RAM.
internal sealed class PackageArchiveStore(long capacity = 256L * 1024 * 1024) : IDisposable
{
    private readonly long capacity = capacity > 0 ? capacity : throw new ArgumentOutOfRangeException(nameof(capacity));
    private readonly object gate = new();
    private readonly Dictionary<GameFile, Entry> entries = new(ReferenceEqualityComparer.Instance);
    private readonly LinkedList<Entry> recent = new();
    private long cachedBytes;
    private bool disposed;
    internal string DirectoryPath { get; } = Directory.CreateTempSubdirectory("asset-index-packages-").FullName;
    public long CachedBytes { get { lock (gate) return cachedBytes; } }
    public long SpooledBytes { get { lock (gate) return entries.Values.Sum(entry => entry.Size); } }

    public PackageArchive Open(GameFile file, VersionContainer versions)
    {
        lock (gate)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            if (!entries.TryGetValue(file, out var entry))
            {
                var bytes = file.Read();
                if (bytes.LongLength != file.Size)
                    throw new InvalidDataException($"Package byte count differs from its directory entry: {file.Path}.");
                entry = new(file.Path, Path.Combine(DirectoryPath, entries.Count.ToString()), file.Size);
                File.WriteAllBytes(entry.File, bytes);
                entries.Add(file, entry);
                Cache(entry, bytes);
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
            if (entry.Size > capacity)
            {
                var result = new byte[length];
                using var stream = File.OpenRead(entry.File);
                stream.Position = position;
                stream.ReadExactly(result);
                return result;
            }
            if (entry.Bytes is null)
            {
                var bytes = File.ReadAllBytes(entry.File);
                if (bytes.LongLength != entry.Size)
                    throw new EndOfStreamException($"Package spool was truncated: {entry.Name}.");
                Cache(entry, bytes);
            }
            recent.Remove(entry.Node!);
            recent.AddFirst(entry.Node!);
            // The returned span keeps this array alive even if another read evicts it.
            return entry.Bytes.AsSpan(checked((int)position), length);
        }
    }

    private void Cache(Entry entry, byte[] bytes)
    {
        if (bytes.LongLength > capacity) return;
        while (cachedBytes > capacity - bytes.LongLength)
        {
            var oldest = recent.Last!.Value;
            cachedBytes -= oldest.Bytes!.LongLength;
            oldest.Bytes = null;
            oldest.Node = null;
            recent.RemoveLast();
        }
        entry.Bytes = bytes;
        entry.Node = recent.AddFirst(entry);
        cachedBytes += bytes.LongLength;
    }

    public void Dispose()
    {
        lock (gate)
        {
            if (disposed) return;
            disposed = true;
            foreach (var entry in recent) { entry.Bytes = null; entry.Node = null; }
            recent.Clear();
            entries.Clear();
            cachedBytes = 0;
            Directory.Delete(DirectoryPath, true);
        }
    }

    internal sealed class Entry(string name, string file, long size)
    {
        public string Name { get; } = name;
        public string File { get; } = file;
        public long Size { get; } = size;
        public byte[]? Bytes;
        public LinkedListNode<Entry>? Node;
    }
}
