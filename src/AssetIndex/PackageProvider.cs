using System.Collections.Concurrent;
using CUE4Parse.FileProvider.Objects;
using CUE4Parse.GameTypes.Theia.FileProvider;
using CUE4Parse.UE4.Assets;
using CUE4Parse.UE4.Assets.Objects;
using CUE4Parse.UE4.IO.Objects;
using CUE4Parse.UE4.Readers;
using CUE4Parse.UE4.Versions;

namespace AssetIndex;

internal class PackageProvider(string directory) : TheiaFileProvider(directory, SearchOption.AllDirectories,
    new VersionContainer(EGame.GAME_ArcRaiders), StringComparer.OrdinalIgnoreCase)
{
    // Import resolution also loads through this provider. Reuse each mounted file,
    // while keeping different archive versions of the same path distinct.
    private readonly ConcurrentDictionary<GameFile, Lazy<IPackage>> packages = new(ReferenceEqualityComparer.Instance);
    private readonly PackageArchiveStore archives = new();
    internal long CachedPackageBytes => archives.CachedBytes;
    internal long SpooledPackageBytes => archives.SpooledBytes;
    internal long PackageSpoolAvailableBytes => new DriveInfo(archives.DirectoryPath).AvailableFreeSpace;

    public override IPackage LoadPackage(GameFile file) =>
        packages.GetOrAdd(file, source => new Lazy<IPackage>(() => ReadPackage(source))).Value;

    public override Task<IPackage> LoadPackageAsync(GameFile file) => Task.Run(() => LoadPackage(file));

    protected virtual IPackage ReadPackage(GameFile file)
    {
        if (!file.IsUePackage) throw new ArgumentException("Cannot load a non-UE package.", nameof(file));
        if (file is not FIoStoreEntry io) return base.LoadPackage(file);
        Files.FindPayloads(file, out _, out var ubulks, out var uptnls);
        Func<FByteBulkDataHeader?, FArchive?>? ubulk = ubulks.Count > 0 ? header => ubulks[0].SafeCreateReader(header) : null;
        Func<FByteBulkDataHeader?, FArchive?>? uptnl = uptnls.Count > 0 ? header => uptnls[0].SafeCreateReader(header) : null;
        return new IoPackage(archives.Open(file, io.IoStoreReader.Versions), io.IoStoreReader.ContainerHeader, ubulk, uptnl, this);
    }

    public override void Dispose()
    {
        packages.Clear();
        try { archives.Dispose(); }
        finally { base.Dispose(); }
    }
}
