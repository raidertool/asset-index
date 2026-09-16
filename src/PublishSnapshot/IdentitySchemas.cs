using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using CUE4Parse.MappingsProvider;
using CUE4Parse.MappingsProvider.Usmap;
using static PublishSnapshot.Preview;

namespace PublishSnapshot;

internal sealed record IdentityProperty(string Name, string Type, int ArraySize = 1, PropertyType? Mapping = null);

internal sealed record IdentitySchema(string SourceClass, string ClassPath,
    IReadOnlyList<string> QualifiedAncestry, IReadOnlyList<string> NativeAncestry,
    IReadOnlyList<IdentityProperty> Properties)
{
    public bool IsA(string name) => NativeAncestry.Contains(name, StringComparer.OrdinalIgnoreCase);

    public IdentityProperty? Property(string name, params string[] types)
    {
        var matches = Properties.Where(property => property.Name.Equals(name, StringComparison.OrdinalIgnoreCase)).ToArray();
        Require(matches.Length <= 1, $"Ambiguous property declaration: {SourceClass}.{name}.");
        if (matches.Length == 0) return null;
        Require(matches[0].ArraySize == 1 && (types.Length == 0 || types.Contains(matches[0].Type)),
            $"Invalid property declaration: {SourceClass}.{name}.");
        return matches[0];
    }
}

// Native declarations come from the pinned usmap. Runtime declarations retain
// their qualified paths and must agree with the separately recorded export header.
internal sealed class IdentitySchemas(TypeMappings mappings)
{
    private sealed record ObjectClass(string Class, string ClassPath, bool Valid);
    private sealed record ExportClass(string ClassPath, string? SuperPath);
    private sealed record RuntimeClass(string? SuperPath, IReadOnlyList<IdentityProperty> Properties);
    private readonly Dictionary<string, ObjectClass> objects = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, ExportClass> exports = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, RuntimeClass> declarations = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, IdentitySchema> resolved = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> classDefaults = new(StringComparer.OrdinalIgnoreCase);

    public bool IsClassDefault(string path) => classDefaults.Contains(path);

    public IdentityProperty? StructProperty(string structure, string name, params string[] types)
    {
        var properties = NativeTypes(structure, requireObjectRoot: false).SelectMany(type => type.Properties.Values.Distinct())
            .Where(property => property.Name.Equals(name, StringComparison.OrdinalIgnoreCase)).ToArray();
        Require(properties.Length <= 1, $"Ambiguous mapped struct property: {structure}.{name}.");
        if (properties.Length == 0) return null;
        var property = properties[0];
        Require(property.ArraySize is null or 1 && (types.Length == 0 || types.Contains(property.MappingType.Type)),
            $"Invalid mapped struct property: {structure}.{name}.");
        return new(property.Name, property.MappingType.Type, property.ArraySize ?? 1, property.MappingType);
    }

    public static TypeMappings LoadMappings(string path, string expectedSha256)
    {
        using var file = File.OpenRead(path);
        var bytes = new byte[file.Length];
        file.ReadExactly(bytes);
        Require(Convert.ToHexStringLower(SHA256.HashData(bytes)) == expectedSha256,
            "Identity mappings differ from the extraction's mapping hash.");
        return new UsmapParser(bytes, comparer: StringComparer.OrdinalIgnoreCase).Mappings
            ?? throw new InvalidDataException("Identity mappings could not be loaded.");
    }

    public void ObserveExport(JsonElement row)
    {
        var path = ResourceEvidence.ObjectPath(row, "path");
        var classPath = ResourceEvidence.ObjectPath(row, "classPath");
        var parent = row.GetProperty("superPath").ValueKind == JsonValueKind.Null
            ? null : ResourceEvidence.ObjectPath(row, "superPath");
        Require(exports.TryAdd(path, new(classPath, parent)), "Duplicate identity export header.");
    }

    public void ObserveObject(JsonElement row)
    {
        var path = ResourceEvidence.ObjectPath(row, "path");
        var classPath = Link(row, "/Class", "class", "resolved");
        Require(classPath is not null, "Decoded object has no qualified class.");
        Require(objects.TryAdd(path, new(String(row, "class"), classPath!, row.GetProperty("issues").GetArrayLength() == 0)),
            "Duplicate identity object.");
        if (row.GetProperty("references").EnumerateArray().Any(edge => String(edge, "pointer") == "/Native/ClassDefaultObject"))
        {
            var classDefault = Link(row, "/Native/ClassDefaultObject", "class-default", "hard");
            if (classDefault is not null) classDefaults.Add(classDefault);
        }
        var declarationValues = row.GetProperty("values").EnumerateArray()
            .Where(value => String(value, "pointer").StartsWith("/Native/ChildProperties", StringComparison.Ordinal)).ToArray();
        if (declarationValues.Length == 0) return;
        var parent = Link(row, "/Native/SuperStruct", "super", "hard");
        declarations.Add(path, new(parent, ReadDeclarations(declarationValues)));
    }

    public IdentitySchema Schema(string path)
    {
        if (resolved.TryGetValue(path, out var cached)) return cached;
        Require(objects.TryGetValue(path, out var source) && source.Valid, $"Complete class evidence is unavailable: {path}.");
        Require(exports.TryGetValue(path, out var header) && header.ClassPath.Equals(source!.ClassPath, StringComparison.OrdinalIgnoreCase),
            $"Decoded class differs from its export header: {path}.");
        var qualified = new List<string>();
        var native = new List<string>();
        var properties = new List<IdentityProperty>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var current = source!.ClassPath;
        while (!current.StartsWith("/Script/", StringComparison.OrdinalIgnoreCase))
        {
            Require(seen.Add(current) && seen.Count <= 128, "Runtime class ancestry repeats or exceeds 128 levels.");
            qualified.Add(current);
            var declaration = RuntimeDeclaration(current);
            properties.AddRange(declaration.Properties);
            current = declaration.SuperPath!;
        }
        qualified.Add(current);
        foreach (var declaration in NativeTypes(ShortName(current)))
        {
            Require(qualified.Count + native.Count < 128, "Combined class ancestry exceeds 128 levels.");
            native.Add(declaration.Name);
            properties.AddRange(declaration.Properties.Values.Distinct().Select(property =>
                new IdentityProperty(property.Name, property.MappingType.Type, property.ArraySize ?? 1, property.MappingType)));
        }
        Require(ShortName(source.ClassPath).Equals(source.Class, StringComparison.OrdinalIgnoreCase),
            $"Decoded class name differs from its qualified path: {path}.");
        return resolved[path] = new(source.Class, source.ClassPath, qualified, native, properties);
    }

    private RuntimeClass RuntimeDeclaration(string path)
    {
        Require(objects.TryGetValue(path, out var body) && body.Valid && declarations.TryGetValue(path, out _),
            $"Runtime property declarations are unavailable: {path}.");
        var declaration = declarations[path];
        Require(declaration.SuperPath is not null, $"Runtime class declaration has no superclass: {path}.");
        Require(exports.TryGetValue(path, out var header) && header.SuperPath is { } parent &&
            parent.Equals(declaration.SuperPath, StringComparison.OrdinalIgnoreCase),
            $"Runtime superclass differs between body and header: {path}.");
        Require(header!.ClassPath.Equals(body!.ClassPath, StringComparison.OrdinalIgnoreCase) &&
            body.ClassPath.StartsWith("/Script/", StringComparison.OrdinalIgnoreCase) &&
            NativeTypes(ShortName(body.ClassPath)).Any(type => type.Name.Equals("Struct", StringComparison.OrdinalIgnoreCase)),
            "Runtime declarations must belong to a native Struct object.");
        return declaration;
    }

    private IEnumerable<Struct> NativeTypes(string name, bool requireObjectRoot = true)
    {
        var current = name;
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        while (true)
        {
            Require(seen.Add(current) && seen.Count <= 128, "Native class ancestry repeats or exceeds 128 levels.");
            Require(mappings.Types.TryGetValue(current, out var declaration) && declaration.Name.Equals(current, StringComparison.OrdinalIgnoreCase),
                $"Native class has no matching mapping: {current}.");
            yield return declaration!;
            if (declaration!.SuperType is null)
            {
                Require(!requireObjectRoot || current.Equals("Object", StringComparison.OrdinalIgnoreCase), "Native class ancestry ends before Object.");
                yield break;
            }
            current = declaration.SuperType;
        }
    }

    private static IReadOnlyList<IdentityProperty> ReadDeclarations(JsonElement[] values)
    {
        const string prefix = "/Native/ChildProperties";
        if (values.Length == 1 && String(values[0], "pointer") == prefix)
        {
            Require(String(values[0], "type") == "FField[]" && String(values[0], "kind") == "empty-array" &&
                values[0].GetProperty("value").ValueKind == JsonValueKind.Null, "Invalid empty runtime declarations.");
            return [];
        }
        var all = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        foreach (var value in values) Require(all.TryAdd(String(value, "pointer"), value), "Duplicate runtime declaration evidence.");
        var roots = values.Where(value => String(value, "pointer").StartsWith(prefix + "/", StringComparison.Ordinal) &&
            !String(value, "pointer")[(prefix.Length + 1)..].Contains('/')).ToArray();
        Require(roots.Length > 0, "Runtime property declarations are incomplete.");
        var result = new List<IdentityProperty>();
        for (var index = 0; index < roots.Length; index++)
            result.Add(ReadDeclaration(all, prefix + "/" + index.ToString(CultureInfo.InvariantCulture)));
        return result;
    }

    private static IdentityProperty ReadDeclaration(IReadOnlyDictionary<string, JsonElement> values, string pointer)
    {
        Require(values.TryGetValue(pointer, out var root), "Runtime declaration ordinals must be contiguous.");
        var type = String(root, "type");
        Require(String(root, "kind") == "field-declaration" && root.GetProperty("value").ValueKind == JsonValueKind.Null &&
            type.StartsWith('F') && type.EndsWith("Property", StringComparison.Ordinal), "Invalid runtime property declaration.");
        var name = DeclarationValue(values, pointer + "/Name", "FName", "name");
        var array = DeclarationValue(values, pointer + "/ArrayDim", "Int32", "integer");
        Require(int.TryParse(array, NumberStyles.None, CultureInfo.InvariantCulture, out var size) && size > 0 &&
            size.ToString(CultureInfo.InvariantCulture) == array, "Invalid runtime property array size.");
        return new(name, type[1..], size);
    }

    private static string DeclarationValue(IReadOnlyDictionary<string, JsonElement> values, string pointer, string type, string kind)
    {
        Require(values.TryGetValue(pointer, out var value) && String(value, "type") == type && String(value, "kind") == kind,
            $"Runtime declaration is incomplete: {pointer}.");
        return String(value, "value");
    }

    private static string? Link(JsonElement row, string pointer, string role, string kind)
    {
        Require(new[] { "values", "texts" }.All(array => !row.GetProperty(array).EnumerateArray().Any(value =>
            String(value, "pointer") == pointer || String(value, "pointer").StartsWith(pointer + "/", StringComparison.Ordinal))),
            $"Class reference has conflicting value evidence: {pointer}.");
        var links = row.GetProperty("references").EnumerateArray().Where(edge => String(edge, "pointer") == pointer).ToArray();
        Require(links.Length == 1 && String(links[0], "kind") == kind && String(links[0], "role") == role &&
            links[0].GetProperty("error").ValueKind == JsonValueKind.Null, $"Invalid class reference: {pointer}.");
        var link = links[0];
        var empty = link.GetProperty("isNull").GetBoolean();
        Require(empty == (link.GetProperty("targetPath").ValueKind == JsonValueKind.Null), "Contradictory class reference.");
        return empty ? null : ResourceEvidence.ObjectPath(link, "targetPath");
    }

    private static string ShortName(string path)
    {
        var split = path.LastIndexOf('.');
        Require(split > 0 && split < path.Length - 1 && !path[(split + 1)..].Contains(':'), "Invalid qualified class path.");
        return path[(split + 1)..];
    }
}
