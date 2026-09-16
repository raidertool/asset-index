using System.Text.Json;
using static PublishSnapshot.Preview;
using static PublishSnapshot.ResourceEvidence;

namespace PublishSnapshot;

// Keep selected property subtrees and template links, not whole decoded packages.
// The structural stream checks run first; these facts support semantic checks.
internal sealed class DecodedEvidence
{
    private readonly Dictionary<string, DecodedObject> objects = new(StringComparer.OrdinalIgnoreCase);
    public IEnumerable<DecodedObject> Objects => objects.Values;

    public static DecodedEvidence Read(SnapshotFile file, IEnumerable<string> rootFields,
        Action<JsonElement>? observe = null)
    {
        var fields = rootFields.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var result = new DecodedEvidence();
        JsonLines.Read(file, row =>
        {
            observe?.Invoke(row);
            var item = DecodedObject.Read(row, fields);
            Require(result.objects.TryAdd(item.Path, item), "Duplicate decoded object path.");
        });
        return result;
    }

    public DecodedObject Object(string path) => objects.TryGetValue(path, out var value) ? value
        : throw new InvalidDataException($"Missing decoded object: {path}.");

}

internal sealed record EvidenceProperty(string Pointer, string Name, string Type, int? ArrayIndex, int? ArraySize,
    string SerializeType = "Property");
internal sealed record EvidenceValue(string Pointer, string Type, string Kind, string? Value);
internal sealed record EvidenceReference(string Pointer, string Kind, string Role, string? TargetPath, bool IsNull, string? Error);

internal sealed record EvidenceField(DecodedObject Owner, EvidenceProperty Header)
{
    public EvidenceField? Child(string name) => Owner.Field(name, Header.Pointer + "/Properties");

    public string? Reference()
    {
        RequireScalar();
        Require(Header.Type is "ObjectProperty" or "SoftObjectProperty", $"Expected object reference: {Owner.Path}.{Header.Name}.");
        Require(!Owner.Values.Any(value => value.Pointer == Header.Pointer) && !Owner.TextPointers.Contains(Header.Pointer),
            "Reference field contains conflicting scalar evidence.");
        var target = Owner.Link(Header.Pointer);
        var reference = Owner.References.Single(value => value.Pointer == Header.Pointer);
        Require(reference.Role == "property" && reference.Kind == (Header.Type == "SoftObjectProperty" ? "soft" : "hard"),
            "Reference kind differs from its property declaration.");
        return target;
    }

    public EvidenceValue Value(string? kind = null)
    {
        RequireScalar();
        var values = Owner.Values.Where(value => value.Pointer == Header.Pointer).ToArray();
        Require(values.Length == 1 && !Owner.References.Any(value => value.Pointer == Header.Pointer) &&
            !Owner.TextPointers.Contains(Header.Pointer), $"Missing or ambiguous scalar: {Owner.Path}.{Header.Name}.");
        Require(kind is null || values[0].Kind == kind, "Scalar evidence has the wrong kind.");
        return values[0];
    }

    private void RequireScalar()
    {
        var prefix = Header.Pointer + "/";
        var pointers = Owner.Properties.Select(value => value.Pointer).Concat(Owner.Values.Select(value => value.Pointer))
            .Concat(Owner.References.Select(value => value.Pointer)).Concat(Owner.TextPointers);
        Require(!pointers.Any(pointer => pointer.StartsWith(prefix, StringComparison.Ordinal)),
            $"Scalar field contains nested evidence: {Owner.Path}.{Header.Name}.");
    }
}

internal sealed record DecodedObject(string Path, string Class, IReadOnlyList<EvidenceProperty> Properties,
    IReadOnlyList<EvidenceValue> Values, IReadOnlyList<EvidenceReference> References, HashSet<string> TextPointers)
{
    public EvidenceField? Field(string name, string container = "/Properties")
    {
        var prefix = container + "/";
        var fields = Properties.Where(field => field.Pointer.StartsWith(prefix, StringComparison.Ordinal) &&
            !field.Pointer[prefix.Length..].Contains('/') && field.Name.Equals(name, StringComparison.OrdinalIgnoreCase)).ToArray();
        Require(fields.Length <= 1, $"Ambiguous field: {Path}.{name}.");
        if (fields.Length == 0) return null;
        var field = fields[0];
        Require(field.ArrayIndex is null or 0 && field.ArraySize is null or 1, $"Field is a static array: {Path}.{name}.");
        Require(field.SerializeType == "Property", $"Field lacks decoded tagged properties: {Path}.{name}.");
        return new(this, field);
    }

    public string? Link(string pointer)
    {
        var links = References.Where(link => link.Pointer == pointer).ToArray();
        Require(links.Length == 1, $"Missing or ambiguous reference: {Path}{pointer}.");
        var link = links[0];
        Require(link.Error is null && link.IsNull == (link.TargetPath is null), $"Unresolved reference: {Path}{pointer}.");
        Require(pointer != "/Template" || link.Role == "template" && link.Kind == "resolved", "Template link has the wrong role or kind.");
        Require(!Values.Any(value => value.Pointer == pointer) && !TextPointers.Contains(pointer),
            "Reference contains conflicting scalar evidence.");
        return link.TargetPath;
    }

    public static DecodedObject Read(JsonElement row, HashSet<string> fields)
    {
        var headers = row.GetProperty("properties").EnumerateArray().ToArray();
        var roots = headers.Where(header => IsRoot(String(header, "pointer")) && fields.Contains(String(header, "name")))
            .Select(header => String(header, "pointer")).ToHashSet(StringComparer.Ordinal);
        bool Wanted(string pointer)
        {
            if (!pointer.StartsWith("/Properties/", StringComparison.Ordinal)) return false;
            var slash = pointer.IndexOf('/', "/Properties/".Length);
            return roots.Contains(slash < 0 ? pointer : pointer[..slash]);
        }
        var properties = headers.Where(header => Wanted(String(header, "pointer"))).Select(header => new EvidenceProperty(
            String(header, "pointer"), String(header, "name"), String(header, "type"),
            Integer(header, "arrayIndex"), Integer(header, "arraySize"), String(header, "serializeType"))).ToArray();
        bool NativeOrWanted(string pointer) => pointer is "/Class" or "/Template" || Wanted(pointer);
        var values = row.GetProperty("values").EnumerateArray().Where(value => NativeOrWanted(String(value, "pointer")))
            .Select(value => new EvidenceValue(String(value, "pointer"), String(value, "type"), String(value, "kind"), Optional(value, "value"))).ToArray();
        var references = row.GetProperty("references").EnumerateArray()
            .Where(value => NativeOrWanted(String(value, "pointer")))
            .Select(value => new EvidenceReference(String(value, "pointer"), String(value, "kind"), String(value, "role"),
                Optional(value, "targetPath"), value.GetProperty("isNull").GetBoolean(), Optional(value, "error"))).ToArray();
        var texts = row.GetProperty("texts").EnumerateArray().Select(value => String(value, "pointer"))
            .Where(NativeOrWanted).ToHashSet(StringComparer.Ordinal);
        foreach (var issue in row.GetProperty("issues").EnumerateArray())
            Require(!Wanted(String(issue, "pointer")), "Selected field has a decode diagnostic.");
        return new(ObjectPath(row, "path"), String(row, "class"), properties, values, references, texts);
    }

    private static bool IsRoot(string pointer) => pointer.StartsWith("/Properties/", StringComparison.Ordinal) &&
        !pointer["/Properties/".Length..].Contains('/');
    private static int? Integer(JsonElement value, string name) => value.GetProperty(name).ValueKind == JsonValueKind.Null
        ? null : value.GetProperty(name).GetInt32();
    private static string? Optional(JsonElement value, string name) => value.GetProperty(name).ValueKind == JsonValueKind.Null
        ? null : value.GetProperty(name).GetString();
}
