using System.Text.Json;
using static PublishSnapshot.Preview;
using static PublishSnapshot.ResourceEvidence;

namespace PublishSnapshot;

// Stream order is arbitrary: a template or table can precede its users. Keep only
// template edges, relevant field presence, requested FText, and requested table keys.
internal sealed class TextOriginEvidence
{
    private sealed record Template(string? Target);
    private sealed record ObjectFacts(string Class, Template? Template, HashSet<string> Fields);
    private sealed record TextFacts(uint Flags, string History, string? Namespace, string? Key, string? Source, string? Table);

    private readonly Dictionary<string, ObjectFacts> objects = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, Dictionary<string, TextFacts>> texts = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, Dictionary<string, TextOriginReference>> tables = new(StringComparer.OrdinalIgnoreCase);

    public static TextOriginEvidence Read(SnapshotFile file, IReadOnlyList<TextOriginCandidate> candidates)
    {
        var result = new TextOriginEvidence();
        var sources = candidates.Select(candidate => candidate.Source).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var wanted = candidates.GroupBy(candidate => candidate.DefinedAt, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.Select(candidate => candidate.Field).ToHashSet(StringComparer.OrdinalIgnoreCase),
                StringComparer.OrdinalIgnoreCase);
        var fields = candidates.Select(candidate => candidate.Field).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var keys = candidates.Select(candidate => candidate.Reference.Key).ToHashSet(StringComparer.Ordinal);
        JsonLines.Read(file, row => result.Observe(row, sources, wanted, fields, keys));
        return result;
    }

    public string SourceClass(string path) => Object(path).Class;

    public string FirstOwner(string source, string field)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (string? path = source; path is not null;)
        {
            Require(seen.Add(path), $"Text property template cycle: {source}.{field}.");
            var current = Object(path);
            if (current.Fields.Contains(field)) return path;
            Require(current.Template is not null, $"Text origin has a missing or ambiguous template link: {path}.");
            path = current.Template!.Target;
        }
        throw new InvalidDataException($"Text property has no source or template owner: {source}.{field}.");
    }

    public TextOriginReference Reference(string owner, string field)
    {
        Require(texts.TryGetValue(owner, out var fields) && fields.ContainsKey(field), $"Missing FText evidence: {owner}.{field}.");
        var text = fields![field];
        var invariant = (text.Flags & 2) != 0;
        return text.History switch
        {
            "Base" => new(Required(text.Namespace), Required(text.Key), Required(text.Source), invariant),
            "None" => new("", "", text.Source ?? "", true),
            "StringTableEntry" => TableReference(text) with { CultureInvariant = invariant },
            _ => throw new InvalidDataException($"Unsupported FText history: {text.History}.")
        };
    }

    private ObjectFacts Object(string path)
    {
        Require(objects.TryGetValue(path, out var value), $"Missing text origin object: {path}.");
        return value!;
    }

    private void Observe(JsonElement row, HashSet<string> sources, Dictionary<string, HashSet<string>> wanted,
        HashSet<string> fields, HashSet<string> keys)
    {
        var path = ObjectPath(row, "path");
        var type = String(row, "class");
        var headers = row.GetProperty("properties").EnumerateArray()
            .Where(header => IsRoot(String(header, "pointer")) && fields.Contains(String(header, "name"))).ToArray();
        Require(objects.TryAdd(path, new(sources.Contains(path) ? type : "", ReadTemplate(row),
            headers.Select(header => String(header, "name")).ToHashSet(StringComparer.OrdinalIgnoreCase))), "Duplicate text origin object.");
        if (wanted.TryGetValue(path, out var selected))
            texts.Add(path, ReadTexts(row, headers, selected));
        if (type.Equals("StringTable", StringComparison.OrdinalIgnoreCase))
            tables.Add(path, ReadTable(row, keys));
    }

    private static Template? ReadTemplate(JsonElement row)
    {
        var edges = row.GetProperty("references").EnumerateArray()
            .Where(edge => String(edge, "pointer") == "/Template").ToArray();
        if (edges.Length != 1) return null;
        var edge = edges[0];
        var isNull = edge.GetProperty("isNull").GetBoolean();
        if (String(edge, "role") != "template" || edge.GetProperty("error").ValueKind != JsonValueKind.Null ||
            isNull != (edge.GetProperty("targetPath").ValueKind == JsonValueKind.Null)) return null;
        return new(isNull ? null : ObjectPath(edge, "targetPath"));
    }

    private static Dictionary<string, TextFacts> ReadTexts(JsonElement row, JsonElement[] headers, HashSet<string> selected)
    {
        var result = new Dictionary<string, TextFacts>(StringComparer.OrdinalIgnoreCase);
        foreach (var group in headers.Where(header => selected.Contains(String(header, "name")))
            .GroupBy(header => String(header, "name"), StringComparer.OrdinalIgnoreCase))
        {
            Require(group.Count() == 1, "Text candidate field has ambiguous property headers.");
            var header = group.Single();
            Require(String(header, "type") == "TextProperty" && Scalar(header), "Text candidate field is not a scalar TextProperty.");
            var pointer = String(header, "pointer");
            var values = row.GetProperty("texts").EnumerateArray().Where(text => String(text, "pointer") == pointer).ToArray();
            Require(values.Length == 1 && !HasOtherScalar(row, pointer), "Text candidate field has missing or ambiguous FText evidence.");
            Require(!row.GetProperty("issues").EnumerateArray().Any(issue =>
                String(issue, "pointer") == pointer || String(issue, "pointer").StartsWith(pointer + "/", StringComparison.Ordinal)),
                "Text candidate field has a decode diagnostic.");
            var text = values[0];
            result.Add(group.Key, new(text.GetProperty("flags").GetUInt32(), String(text, "history"),
                Optional(text, "namespace"), Optional(text, "key"), Optional(text, "source"), Optional(text, "tableId")));
        }
        return result;
    }

    private static bool HasOtherScalar(JsonElement row, string pointer) =>
        new[] { "values", "references" }.Any(kind => row.GetProperty(kind).EnumerateArray().Any(value => String(value, "pointer") == pointer));

    private static bool Scalar(JsonElement header) =>
        (header.GetProperty("arrayIndex").ValueKind == JsonValueKind.Null || header.GetProperty("arrayIndex").GetInt32() == 0) &&
        (header.GetProperty("arraySize").ValueKind == JsonValueKind.Null || header.GetProperty("arraySize").GetInt32() == 1);

    private static Dictionary<string, TextOriginReference> ReadTable(JsonElement row, HashSet<string> keys)
    {
        var result = new Dictionary<string, TextOriginReference>(StringComparer.Ordinal);
        foreach (var entry in row.GetProperty("tableEntries").EnumerateArray())
        {
            var key = String(entry, "key", allowEmpty: true);
            if (!keys.Contains(key)) continue;
            Require(String(entry, "pointer") == "/StringTable/" + key.Replace("~", "~0").Replace("/", "~1"),
                "String-table entry pointer differs from its key.");
            Require(result.TryAdd(key, new(String(entry, "namespace", allowEmpty: true), key,
                String(entry, "source", allowEmpty: true), false)), "Duplicate string-table key for a text candidate.");
        }
        return result;
    }

    private TextOriginReference TableReference(TextFacts text)
    {
        var table = Required(text.Table);
        Require(table.StartsWith('/') && table[(table.LastIndexOf('/') + 1)..].Contains('.'),
            "String-table FText must identify an exact table object path.");
        Require(tables.TryGetValue(table, out var entries) && entries.ContainsKey(Required(text.Key)),
            "String-table FText has no matching table object and exact key.");
        return entries![text.Key!];
    }

    private static bool IsRoot(string pointer) => pointer.StartsWith("/Properties/", StringComparison.Ordinal) &&
        !pointer["/Properties/".Length..].Contains('/');

    private static string? Optional(JsonElement value, string field) => value.GetProperty(field).ValueKind == JsonValueKind.Null
        ? null : String(value, field, allowEmpty: true);

    private static string Required(string? value) => value ?? throw new InvalidDataException("Required FText evidence is null.");
}
