using CUE4Parse.MappingsProvider;
using CUE4Parse.UE4.Assets.Exports;

namespace AssetIndex;

internal static class TextRoles
{
    public static IReadOnlyList<TextField> For(UObject source, TypeMappings? mappings)
    {
        if (mappings is null) throw new InvalidDataException($"Type mappings are unavailable for {source.GetPathName()}.");
        var schema = ClassSchema.Read(source, mappings);
        var fields = TextRolePolicy.For(schema.NativeAncestry);
        foreach (var field in fields)
            if (!schema.HasProperty(field.Name, "TextProperty"))
                throw new InvalidDataException($"Text role has no property declaration: {source.ExportType}.{field.Name}.");
        return fields;
    }
}
