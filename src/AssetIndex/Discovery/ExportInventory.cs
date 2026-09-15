using CUE4Parse.MappingsProvider;
using CUE4Parse.UE4.Assets;
using CUE4Parse.UE4.Objects.UObject;

namespace AssetIndex.Discovery;

internal sealed record ExportHeader(string Package, int Index, string? Path, string? Class, string? ClassPath,
    IReadOnlyList<string> Ancestry, bool AncestryComplete, string? Error, string? SuperPath = null);

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
        string? path = null, type = null, classPath = null, superPath = null, error = null;
        var ancestry = new List<string>();
        var complete = false;
        try
        {
            var export = package.ResolvePackageIndex(new FPackageIndex(package, index + 1))
                ?? throw new InvalidDataException("Export index has no resolved metadata.");
            path = ObjectMetadata.Path(export);
            var parent = ObjectMetadata.Super(export);
            superPath = parent is null ? null : ObjectMetadata.Path(parent);
            var declaration = export.Class ?? throw new InvalidDataException("Export class metadata is missing.");
            type = declaration.Name.Text;
            classPath = ObjectMetadata.Path(declaration);
            var schema = ClassSchema.Read(declaration, mappings);
            ancestry.AddRange(schema.Ancestry);
            error = schema.Error;
            complete = error is null;
        }
        catch (Exception exception) { error = AssetDiscovery.DescribeError(exception); }
        return new(physicalPath, index, path, type, classPath, ancestry.ToArray(), complete, error, superPath);
    }

}
