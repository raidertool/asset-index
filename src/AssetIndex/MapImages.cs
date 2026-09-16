using CUE4Parse.MappingsProvider;
using CUE4Parse.UE4.Assets.Exports;
using CUE4Parse.UE4.Assets.Objects;
using CUE4Parse.UE4.Assets.Objects.Properties;

namespace AssetIndex;

// These two declared structs contain catalog images. Other map references,
// including MapMaterial, remain discovery evidence rather than rendered map state.
internal static class MapImages
{
    public static void Capture(UObject source, TypeMappings mappings, Func<UObject, ObjectLocation> locate,
        ICollection<ImageRequest> images, ICollection<ExtractionIssue> issues)
    {
        var path = ObjectMetadata.Path(source);
        foreach (var contract in ImageFields.Nested)
        {
            var field = contract.Root;
            try
            {
                if (Properties.Find(source, field) is not { } property) continue;
                field = property.Name.Text;
                var schema = ClassSchema.Read(source, mappings);
                if (!schema.IsA(contract.Owner)) continue;
                RequireDeclaration(schema, mappings, contract);
                foreach (var entry in Entries(property, contract))
                    CaptureLeaf(entry.Value, entry.Field, path, contract.Leaf, locate, images, issues);
            }
            catch (Exception error) { images.Add(Images.Failed(field, path, null, error, issues)); }
        }
    }

    private static IEnumerable<(string Field, FPropertyTagType? Value)> Entries(FPropertyTag property, NestedImageField contract)
    {
        var field = property.Name.Text;
        if (!contract.IsArray)
        {
            if (!Same(property.TagData?.StructType, contract.Structure))
                throw new InvalidDataException($"Unexpected struct type for {field}.");
            yield return (field, property.Tag);
            yield break;
        }
        if (property.Tag is not ArrayProperty { Value: { } values } || !Same(values.InnerType, "StructProperty")
            || values.Properties.Count != 0 && !Same(values.InnerTagData?.StructType, contract.Structure))
            throw new InvalidDataException($"Expected an array of {contract.Structure} for {field}.");
        for (var index = 0; index < values.Properties.Count; index++)
            yield return ($"{field}[{index}]", values.Properties[index]);
    }

    private static void CaptureLeaf(FPropertyTagType? value, string field, string path, string leaf,
        Func<UObject, ObjectLocation> locate, ICollection<ImageRequest> images, ICollection<ExtractionIssue> issues)
    {
        try
        {
            if (value is not StructProperty { Value.StructType: FStructFallback structure })
                throw new InvalidDataException($"Expected a decoded struct at {path}.{field}.");
            if (Properties.Find(structure, leaf, $"{path}.{field}") is not { } property) return;
            field += "." + property.Name.Text;
            if (property.Tag is not SoftObjectProperty)
                throw new InvalidDataException($"Expected a soft object reference at {path}.{field}.");
            images.Add(Images.CaptureReference(property, field, path, locate, issues));
        }
        catch (Exception error) { images.Add(Images.Failed(field, path, null, error, issues)); }
    }

    private static void RequireDeclaration(ClassSchema schema, TypeMappings mappings, NestedImageField contract)
    {
        var kind = contract.IsArray ? "ArrayProperty" : "StructProperty";
        if (!schema.HasProperty(contract.Root, kind))
            throw new InvalidDataException($"No declared {contract.Owner}.{contract.Root} image container.");
        var container = DeclaredField(mappings, contract.Owner, contract.Root);
        var nested = contract.IsArray ? container.InnerType : container;
        if (!Same(container.Type, kind) || !Same(nested?.Type, "StructProperty") || !Same(nested?.StructType, contract.Structure))
            throw new InvalidDataException($"Unexpected declaration for {contract.Owner}.{contract.Root}.");
        if (!Same(DeclaredField(mappings, contract.Structure, contract.Leaf).Type, "SoftObjectProperty"))
            throw new InvalidDataException($"Unexpected declaration for {contract.Structure}.{contract.Leaf}.");
    }

    private static PropertyType DeclaredField(TypeMappings mappings, string type, string name)
    {
        var fields = new List<PropertyInfo>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (string? current = type; current is not null;)
        {
            if (!seen.Add(current) || seen.Count > 128 || !mappings.Types.TryGetValue(current, out var declaration))
                throw new InvalidDataException($"Incomplete or cyclic image declaration {type}.");
            fields.AddRange(declaration.Properties.Values.Distinct().Where(field => Same(field.Name, name)));
            current = declaration.SuperType;
        }
        if (fields.Count != 1 || fields[0].ArraySize is > 1)
            throw new InvalidDataException($"Ambiguous or missing scalar declaration {type}.{name}.");
        return fields[0].MappingType;
    }

    private static bool Same(string? actual, string expected) => string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase);
}
