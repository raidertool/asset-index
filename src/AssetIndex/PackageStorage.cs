using CUE4Parse.FileProvider.Objects;
using CUE4Parse.UE4.IO.Objects;

namespace AssetIndex;

internal sealed record PackageStorage(int Packages, long Bytes)
{
    public static PackageStorage Read(IEnumerable<GameFile> mountedFiles)
    {
        // Imports can load shadowed versions; spool ownership uses GameFile identity.
        var packages = mountedFiles.OfType<FIoStoreEntry>().Where(file => file.IsUePackage)
            .Distinct<FIoStoreEntry>(ReferenceEqualityComparer.Instance).ToArray();
        return new(packages.Length, packages.Sum(file => file.Size));
    }
}
