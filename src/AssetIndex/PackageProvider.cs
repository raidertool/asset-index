using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;
using CUE4Parse.FileProvider.Objects;
using CUE4Parse.GameTypes.Theia.FileProvider;
using CUE4Parse.UE4.Assets;
using CUE4Parse.UE4.Assets.Exports;
using CUE4Parse.UE4.Assets.Objects;
using CUE4Parse.UE4.IO;
using CUE4Parse.UE4.IO.Objects;
using CUE4Parse.UE4.Readers;
using CUE4Parse.UE4.Versions;

namespace AssetIndex;

internal class PackageProvider(string directory) : TheiaFileProvider(directory, SearchOption.AllDirectories,
    new VersionContainer(EGame.GAME_ArcRaiders), StringComparer.OrdinalIgnoreCase)
{
    // Imports reuse live packages without retaining decoded graphs. Proven
    // equivalent peers share one physical owner; other archive versions remain
    // distinct. Their page spool survives reloads.
    private readonly ConcurrentDictionary<GameFile, PackageEntry> packages = new(ReferenceEqualityComparer.Instance);
    private readonly ConditionalWeakTable<IPackage, GameFile> packageFiles = new();
    private readonly ConcurrentDictionary<ulong, byte> loadedPackageIds = new();
    private readonly PackageArchiveStore archives = new();
    private IReadOnlyDictionary<GameFile, GameFile> equivalentFiles = new Dictionary<GameFile, GameFile>();
    private bool disposed;
    private Discovery.PackageInputIndex? inputIndex;
    internal Discovery.PackageInputIndex InputIndex => inputIndex ??= new(this);
    internal bool HasLoadedPackageId(ulong id) => loadedPackageIds.ContainsKey(id);
    internal long CachedPackageBytes => archives.CachedBytes;
    internal long SpooledPackageBytes => archives.SpooledBytes;
    internal long PackageSpoolAvailableBytes => new DriveInfo(archives.DirectoryPath).AvailableFreeSpace;
    internal string? MappingSha256 { get; set; }

    internal void NormalizeFiles(Func<IoStoreReader, FIoStoreTocResource>? readToc = null)
    {
        if (!packages.IsEmpty || inputIndex is not null)
            throw new InvalidOperationException("Mount precedence must be established before reading packages.");
        equivalentFiles = MountPrecedence.Apply(Files, PathComparer, readToc);
    }

    public override IPackage LoadPackage(GameFile file)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        file = equivalentFiles.GetValueOrDefault(file, file);
        return packages.GetOrAdd(file, _ => new PackageEntry()).Load(() =>
        {
            var package = ReadPackage(file);
            if (!ReferenceEquals(packageFiles.GetValue(package, _ => file), file))
                throw new InvalidDataException("Package instance maps to multiple physical files.");
            loadedPackageIds.TryAdd(FPackageId.FromName(package.Name).id, 0);
            return package;
        });
    }

    public override Task<IPackage> LoadPackageAsync(GameFile file) => Task.Run(() => LoadPackage(file));

    internal GameFile? SourceFile(IPackage package) => packageFiles.TryGetValue(package, out var file) ? file : null;

    internal ObjectLocation Locate(UObject source)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        var owner = source.Owner;
        if (owner is null || !packageFiles.TryGetValue(owner, out var file))
            throw new InvalidDataException("Object does not belong to a package loaded by this provider.");
        var index = -1;
        for (var candidate = 0; candidate < owner.ExportsLazy.Length; candidate++)
        {
            var export = owner.ExportsLazy[candidate];
            if (export is null || !export.IsValueCreated || !ReferenceEquals(export.Value, source)) continue;
            if (index >= 0) throw new InvalidDataException("Object occurs at multiple export indices.");
            index = candidate;
        }
        if (index < 0 || index >= owner.ExportMapLength)
            throw new InvalidDataException("Object has no unique decoded export index in its package.");
        return new(file, index, ObjectMetadata.Path(new ResolvedLoadedObject(source)));
    }

    internal UObject Load(ObjectLocation location)
    {
        var package = LoadPackage(location.File);
        var index = location.ExportIndex;
        if (index < 0 || index >= package.ExportMapLength || index >= package.ExportsLazy.Length || package.ExportsLazy[index] is null)
            throw new InvalidDataException("Object locator export index is outside its package.");
        var source = package.ExportsLazy[index].Value;
        if (!ReferenceEquals(source.Owner, package) ||
            !ObjectMetadata.Path(new ResolvedLoadedObject(source)).Equals(location.Path, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Object locator does not match the loaded export path.");
        return source;
    }

    protected virtual IPackage ReadPackage(GameFile file)
    {
        if (!file.IsUePackage) throw new ArgumentException("Cannot load a non-UE package.", nameof(file));
        if (file is not FIoStoreEntry io) return base.LoadPackage(file);
        var payloads = MountPrecedence.ResolvePayloads(io, Files);
        Func<FByteBulkDataHeader?, FArchive?>? ubulk = payloads.Bulk is { } bulk ? header => bulk.SafeCreateReader(header) : null;
        Func<FByteBulkDataHeader?, FArchive?>? uptnl = payloads.Optional is { } optional ? header => optional.SafeCreateReader(header) : null;
        var package = new IoPackage(archives.Open(file, io.IoStoreReader.Versions,
            (offset, count) => ReadRange(io, offset, count)), io.IoStoreReader.ContainerHeader, ubulk, uptnl, this);
        MountPrecedence.RequirePackageIdentity(io, package.Name);
        return package;
    }

    internal static byte[] ReadRange(FIoStoreEntry file, long offset, int count)
    {
        if (offset < 0 || count < 0 || offset > file.Size - count)
            throw new EndOfStreamException($"Read exceeds package bounds: {file.Path}.");
        if (count == 0) return [];
        // Upstream IoStore treats these two header fields as a package-relative range.
        // Its container reader still owns block selection, decryption and decompression.
        var bytes = file.Read(new FByteBulkDataHeader(0, count, (uint)count, offset, FBulkDataCookedIndex.Default));
        if (bytes.Length != count)
            throw new InvalidDataException($"IoStore reader returned an unexpected range length: {file.Path}.");
        return bytes;
    }

    public override void Dispose()
    {
        disposed = true;
        equivalentFiles = new Dictionary<GameFile, GameFile>();
        packages.Clear();
        packageFiles.Clear();
        try { archives.Dispose(); }
        finally { base.Dispose(); }
    }

    private sealed class PackageEntry
    {
        private WeakReference<IPackage>? package;
        private ExceptionDispatchInfo? failure;
        private bool loading;

        public IPackage Load(Func<IPackage> read)
        {
            lock (this)
            {
                failure?.Throw();
                if (package is not null && package.TryGetTarget(out var existing)) return existing;
                if (loading) throw new InvalidOperationException("Recursive construction of the same package.");
                loading = true;
                try
                {
                    var result = read();
                    package = new(result);
                    return result;
                }
                catch (Exception error)
                {
                    failure = ExceptionDispatchInfo.Capture(error);
                    throw;
                }
                finally { loading = false; }
            }
        }
    }
}
