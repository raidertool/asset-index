using System.Text;
using CUE4Parse.MappingsProvider;
using CUE4Parse.UE4.Assets;
using CUE4Parse.UE4.Objects.UObject;

namespace AssetIndex.Discovery;

internal sealed record ExportHeader(string Package, int Index, string? Path, string? Class, string? ClassPath,
    IReadOnlyList<string> Ancestry, bool AncestryComplete, string? Error);

internal static class ExportInventory
{
    public static IReadOnlyList<ExportHeader> Read(IPackage package, string physicalPackagePath, TypeMappings mappings)
    {
        var result = new ExportHeader[package.ExportMapLength];
        for (var index = 0; index < result.Length; index++)
            result[index] = ReadHeader(package, physicalPackagePath, index, mappings);

        foreach (var group in result.Where(header => header.Path is not null)
            .GroupBy(header => header.Path!, StringComparer.OrdinalIgnoreCase).Where(group => group.Count() > 1))
        {
            var error = $"Ambiguous export path {group.Key}; indices {string.Join(", ", group.Select(header => header.Index))}.";
            foreach (var header in group)
                result[header.Index] = header with { Error = header.Error is null ? error : header.Error + " " + error };
        }
        return result;
    }

    private static ExportHeader ReadHeader(IPackage package, string physicalPath, int index, TypeMappings mappings)
    {
        string? path = null, type = null, classPath = null, error = null;
        var ancestry = new List<string>();
        var complete = false;
        try
        {
            var export = package.ResolvePackageIndex(new FPackageIndex(package, index + 1))
                ?? throw new InvalidDataException("Export index has no resolved metadata.");
            path = FullPath(export);
            var declaration = export.Class ?? throw new InvalidDataException("Export class metadata is missing.");
            type = declaration.Name.Text;
            classPath = FullPath(declaration);
            ReadAncestry(declaration, mappings, ancestry);
            complete = true;
        }
        catch (Exception exception) { error = AssetDiscovery.DescribeError(exception); }
        return new(physicalPath, index, path, type, classPath, ancestry.ToArray(), complete, error);
    }

    private static void ReadAncestry(ResolvedObject declaration, TypeMappings mappings, List<string> names)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        while (true)
        {
            if (!seen.Add(FullPath(declaration)) || names.Count >= 128)
                throw new InvalidDataException("Runtime class ancestry repeats or exceeds 128 levels.");
            names.Add(declaration.Name.Text);
            if (ReadSuper(declaration) is not { } parent) break;
            declaration = parent;
        }

        // Script imports do not expose their native superclass. Finish that chain
        // from usmap; a missing runtime Super pointer alone is not root evidence.
        var current = names[^1];
        var mapped = new HashSet<string>(names.SkipLast(1), StringComparer.OrdinalIgnoreCase);
        while (true)
        {
            if (!mapped.Add(current) || names.Count > 128)
                throw new InvalidDataException("Mapped class ancestry repeats or exceeds 128 levels.");
            if (!mappings.Types.TryGetValue(current, out var schema))
                throw new InvalidDataException($"Class ancestry has no mapping or superclass for {current}.");
            if (schema.SuperType is null)
            {
                if (!current.Equals("Object", StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException($"Mapped class ancestry ends before Object at {current}.");
                return;
            }
            current = schema.SuperType;
            names.Add(current);
        }
    }

    private static string FullPath(ResolvedObject reference)
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

    private static ResolvedObject? ReadSuper(ResolvedObject reference)
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
