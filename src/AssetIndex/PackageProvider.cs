using System.Collections.Concurrent;
using CUE4Parse.FileProvider.Objects;
using CUE4Parse.GameTypes.Theia.FileProvider;
using CUE4Parse.UE4.Assets;
using CUE4Parse.UE4.Versions;

namespace AssetIndex;

internal class PackageProvider(string directory) : TheiaFileProvider(directory, SearchOption.AllDirectories,
    new VersionContainer(EGame.GAME_ArcRaiders), StringComparer.OrdinalIgnoreCase)
{
    // Import resolution also loads through this provider. Reuse each mounted file,
    // while keeping different archive versions of the same path distinct.
    private readonly ConcurrentDictionary<GameFile, Lazy<IPackage>> packages = new(ReferenceEqualityComparer.Instance);

    public override IPackage LoadPackage(GameFile file) =>
        packages.GetOrAdd(file, source => new Lazy<IPackage>(() => ReadPackage(source))).Value;

    public override Task<IPackage> LoadPackageAsync(GameFile file) => Task.Run(() => LoadPackage(file));

    protected virtual IPackage ReadPackage(GameFile file) => base.LoadPackage(file);

    public override void Dispose()
    {
        packages.Clear();
        base.Dispose();
    }
}
