using System.Text;
using CUE4Parse.UE4.Assets;
using CUE4Parse.UE4.Assets.Exports;

namespace AssetIndex;

internal static class ObjectMetadata
{
    public static string Path(UObject source) => source.Outer is null ? source.Name : Path(new ResolvedLoadedObject(source));

    public static string Path(ResolvedObject reference)
    {
        var chain = new List<ResolvedObject>();
        var exports = new HashSet<(IPackage Package, int Index)>();
        for (var current = reference; current is not null; current = ReadOuter(current))
        {
            if (current.ExportIndex >= current.Package.ExportMapLength)
                throw new InvalidDataException("Export metadata index is outside its package header.");
            if (chain.Count >= 128 || current.ExportIndex >= 0 && !exports.Add((current.Package, current.ExportIndex)))
                throw new InvalidDataException("Export outer chain repeats or exceeds 128 levels.");
            if (current.Name.IsNone || current.Name.Text.Length == 0)
                throw new InvalidDataException("Export outer chain contains an empty name.");
            chain.Add(current);
        }
        if (chain[^1] is not ResolvedPackageObject && !chain[^1].Name.Text.StartsWith('/'))
            throw new InvalidDataException("Export outer chain has no package root.");
        var path = new StringBuilder(chain[^1].Name.Text);
        for (var index = chain.Count - 2; index >= 0; index--)
            path.Append(index == chain.Count - 3 ? ':' : '.').Append(chain[index].Name.Text);
        return path.ToString();
    }

    private static ResolvedObject? ReadOuter(ResolvedObject reference)
    {
        if (reference.ExportIndex < 0) return reference.Outer;
        if (reference.Package is IoPackage io)
        {
            var index = io.ExportMap[reference.ExportIndex].OuterIndex;
            return index.IsNull ? new ResolvedPackageObject(io) : io.ResolveObjectIndex(index)
                ?? throw new InvalidDataException("Serialized outer index could not be resolved.");
        }
        if (reference.Package is Package package)
        {
            var index = package.ExportMap[reference.ExportIndex].OuterIndex;
            return index is null || index.IsNull ? new ResolvedPackageObject(package) : package.ResolvePackageIndex(index)
                ?? throw new InvalidDataException("Serialized outer index could not be resolved.");
        }
        return reference.Outer;
    }

    public static ResolvedObject? Super(ResolvedObject reference)
    {
        if (reference.ExportIndex < 0) return reference.Super;
        if (reference.Package is IoPackage io)
        {
            var index = io.ExportMap[reference.ExportIndex].SuperIndex;
            if (index.IsNull) return null;
            return io.ResolveObjectIndex(index) ?? throw new InvalidDataException("Serialized superclass index could not be resolved.");
        }
        if (reference.Package is Package package)
        {
            var index = package.ExportMap[reference.ExportIndex].SuperIndex;
            if (index.IsNull) return null;
            return package.ResolvePackageIndex(index) ?? throw new InvalidDataException("Serialized superclass index could not be resolved.");
        }
        return reference.Super;
    }
}
