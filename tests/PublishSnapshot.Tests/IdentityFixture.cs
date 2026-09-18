using System.IO.Compression;
using System.Text.Json;
using System.Text.Json.Nodes;
using CUE4Parse.MappingsProvider;

namespace PublishSnapshot.Tests;

internal sealed class IdentityFixture : IDisposable
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), "identity-tests-" + Guid.NewGuid().ToString("N"));
    public TypeMappings Mappings { get; } = new();
    public List<JsonObject> Objects { get; } = [];
    public List<JsonObject> Exports { get; } = [];

    public IdentityFixture()
    {
        AddClass("Object", null);
        AddClass("Field", "Object");
        AddClass("Struct", "Field");
        AddClass("Class", "Struct");
        AddClass("BlueprintGeneratedClass", "Class");
        AddClass("DataAsset", "Object");
        AddClass("PersistenceDataAsset", "DataAsset", ("AssetId", "Int64Property"));
        AddClass("OptionalPersistenceDataAsset", "DataAsset", ("AssetId", "Int64Property"));
        AddClass("ItemDataAssetBase", "DataAsset", ("bOverrideItemAssetId", "BoolProperty"),
            ("OverrideItemAssetId", "Int64Property"), ("PersistenceDataAsset", "ObjectProperty"));
        AddClass("UIMetaDataItem", "Object", ("bOverrideAssetId", "BoolProperty"),
            ("OverrideAssetId", "Int64Property"), ("PersistenceDataAsset", "ObjectProperty"));
    }

    public void AddClass(string name, string? parent, params (string Name, string Type)[] properties)
    {
        var fields = properties.Select((value, index) => new PropertyInfo(index, value.Name, new PropertyType(value.Type), 1))
            .ToDictionary(property => property.Index);
        Mappings.Types.Add(name, new(Mappings, name, parent, fields, fields.Count));
    }

    public JsonObject Object(string path, string type, string? template = null, string? classPath = null)
    {
        classPath ??= "/Script/Test." + type;
        var row = new JsonObject
        {
            ["path"] = path,
            ["class"] = type,
            ["properties"] = new JsonArray(),
            ["values"] = new JsonArray(),
            ["texts"] = new JsonArray(),
            ["issues"] = new JsonArray(),
            ["tableEntries"] = new JsonArray(),
            ["references"] = new JsonArray(Link("/Class", classPath, "class", "resolved"), Link("/Template", template, "template", "resolved"))
        };
        Objects.Add(row);
        Exports.Add(new JsonObject { ["path"] = path, ["classPath"] = classPath, ["superPath"] = null });
        return row;
    }

    public (DecodedEvidence Evidence, IdentitySchemas Schemas, IdentityContext Context) Read(params string[] fields)
    {
        Directory.CreateDirectory(directory);
        var file = Path.Combine(directory, "objects.jsonl.gz");
        using (var stream = File.Create(file))
        using (var gzip = new GZipStream(stream, CompressionLevel.Fastest))
        using (var writer = new StreamWriter(gzip))
            foreach (var row in Objects) writer.WriteLine(row.ToJsonString());
        var schemas = new IdentitySchemas(Mappings);
        foreach (var row in Exports)
        {
            using var document = JsonDocument.Parse(row.ToJsonString());
            schemas.ObserveExport(document.RootElement);
        }
        var evidence = DecodedEvidence.Read(SnapshotFile.Read(file), IdentityContext.RootFields.Concat(fields), schemas.ObserveObject);
        return (evidence, schemas, new(evidence, schemas));
    }

    public static JsonObject Catalog(string id, JsonObject[] definitions, JsonObject[]? metadata = null) => new()
    {
        ["id"] = id,
        ["definitions"] = new JsonArray(definitions.Select(Source).ToArray()),
        ["metadata"] = new JsonArray((metadata ?? []).Select(Source).ToArray())
    };

    public static void Validate(IdentityContext context, params JsonObject[] assets)
    {
        using var document = JsonDocument.Parse(new JsonArray(assets.Select(asset => asset.DeepClone()).ToArray()).ToJsonString());
        context.Validate(document.RootElement);
    }

    public static JsonNode Source(JsonObject row) => new JsonObject
    {
        ["name"] = row["path"]!.GetValue<string>().Split('.', ':').Last(),
        ["class"] = row["class"]!.GetValue<string>(),
        ["path"] = row["path"]!.GetValue<string>()
    };

    public static string Header(JsonObject row, string name, string type, string container = "/Properties")
    {
        var prefix = container + "/";
        var count = row["properties"]!.AsArray().Count(value =>
        {
            var pointer = value!["pointer"]!.GetValue<string>();
            return pointer.StartsWith(prefix, StringComparison.Ordinal) && !pointer[prefix.Length..].Contains('/');
        });
        var pointer = container + "/" + count;
        row["properties"]!.AsArray().Add(new JsonObject
        {
            ["pointer"] = pointer,
            ["name"] = name,
            ["type"] = type,
            ["arrayIndex"] = null,
            ["arraySize"] = 1,
            ["serializeType"] = "Property"
        });
        return pointer;
    }

    public static void Number(JsonObject row, string name, string value) => Scalar(row, name, "Int64Property", "integer", value);
    public static void Boolean(JsonObject row, string name, bool value) => Scalar(row, name, "BoolProperty", "boolean", value ? "true" : "false");
    public static void Scalar(JsonObject row, string name, string type, string kind, string? value) =>
        Value(row, Header(row, name, type), type, kind, value);
    public static void Value(JsonObject row, string pointer, string type, string kind, string? value) =>
        row["values"]!.AsArray().Add(new JsonObject { ["pointer"] = pointer, ["type"] = type, ["kind"] = kind, ["value"] = value });
    public static void Reference(JsonObject row, string name, string? target, bool soft = false) =>
        row["references"]!.AsArray().Add(Link(Header(row, name, soft ? "SoftObjectProperty" : "ObjectProperty"), target, "property", soft ? "soft" : "hard"));
    public static JsonObject Link(string pointer, string? target, string role, string kind) => new()
    {
        ["pointer"] = pointer,
        ["kind"] = kind,
        ["role"] = role,
        ["targetPath"] = target,
        ["isNull"] = target is null,
        ["error"] = null
    };

    public void Dispose()
    {
        if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
    }
}
