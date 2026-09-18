using AssetIndex;
using System.Buffers.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using SkiaSharp;
using static PublishSnapshot.Preview;

namespace PublishSnapshot;

internal sealed record ResourceImage(string File, int Width, int Height);
internal sealed record ResourceEvidence(HashSet<string> ObjectPaths, IReadOnlyList<LocalizationEvidence> Localizations,
    IReadOnlyDictionary<string, ResourceImage> Resources)
{
    public static ResourceEvidence Read(IReadOnlyDictionary<string, SnapshotFile> files, JsonElement report)
    {
        var discovery = report.GetProperty("discovery");
        Fields(discovery, discovery.TryGetProperty("unavailableSoftReferences", out _)
            ? (discovery.TryGetProperty("inputContainers", out _)
                ? ["nativeScope", "mappingSha256", "objects", "resources", "unavailableSoftReferences", "unavailableHardReferences", "unmappedNonCatalogExports", "inputContainers"]
                : ["nativeScope", "mappingSha256", "objects", "resources", "unavailableSoftReferences", "unavailableHardReferences", "unmappedNonCatalogExports"])
            : ["nativeScope", "mappingSha256", "objects", "resources"]);
        String(discovery, "nativeScope");
        Require(Regex.IsMatch(String(discovery, "mappingSha256"), "\\A[0-9a-f]{64}\\z"), "Invalid mapping hash.");
        foreach (var notice in report.GetProperty("notices").EnumerateArray())
        {
            Fields(notice, "stage", "path", "message");
            foreach (var field in new[] { "stage", "path", "message" }) String(notice, field);
        }
        var targets = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var requiredBodies = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var unavailable = new UnavailableInputs(files);
        var objectTypes = ReadObjects(files["discovery/objects.jsonl.gz"], targets, requiredBodies, unavailable);
        var objects = objectTypes.Keys.ToHashSet(StringComparer.OrdinalIgnoreCase);
        CheckCount(discovery, "objects", objects.Count);
        var registry = new HashSet<string>(StringComparer.Ordinal);
        var registryPackages = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var uiTextures = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        JsonLines.Read(files["discovery/registry.jsonl.gz"], row =>
        {
            Fields(row, "path", "package", "class", "tags");
            Require(registry.Add(ObjectPath(row, "path")), "Duplicate registry object.");
            registryPackages.Add(ObjectPath(row, "package"));
            String(row, "class");
            var tags = new HashSet<string>(StringComparer.Ordinal);
            foreach (var tag in row.GetProperty("tags").EnumerateObject())
                Require(tags.Add(tag.Name) && tag.Value.ValueKind == JsonValueKind.String, "Invalid registry tag.");
            if (String(row, "class") == "Texture2D" && row.GetProperty("tags").TryGetProperty("LODGroup", out var group) && group.GetString() == "TEXTUREGROUP_UI")
                uiTextures.Add(ObjectPath(row, "path"));
        });
        CheckCount(report, "registeredAssets", registry.Count);
        var inputs = MountedInputs.Read(files["discovery/files.jsonl.gz"], registryPackages);
        unavailable.Complete(inputs, discovery);
        ExportCoverage.Validate(files, report, inputs, objectTypes, targets, uiTextures, requiredBodies);
        var locales = ReadLocalizations(files);
        var resources = ReadResources(files, objects.Contains);
        Require(uiTextures.IsSubsetOf(resources.Keys), "A registry UI texture lacks a published resource.");
        CheckCount(discovery, "resources", resources.Count);
        return new(objects, locales, resources);
    }

    private static Dictionary<string, string> ReadObjects(SnapshotFile file, HashSet<string> targets,
        HashSet<string> requiredBodies, UnavailableInputs unavailable)
    {
        var objects = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        JsonLines.Read(file, row =>
        {
            Fields(row, "path", "class", "properties", "references", "texts", "values", "tableEntries", "issues");
            Require(objects.TryAdd(ObjectPath(row, "path"), String(row, "class")), "Ambiguous discovered object paths differ only by case or repeat.");
            Require(row.GetProperty("issues").GetArrayLength() == 0, "Object evidence contains diagnostics.");
            var properties = PropertyHeaders.Read(row.GetProperty("properties"));
            foreach (var reference in row.GetProperty("references").EnumerateArray())
            {
                Fields(reference, reference.TryGetProperty("unavailable", out _)
                    ? ["pointer", "kind", "role", "targetPath", "isNull", "package", "packageIndex", "exportIndex", "error", "unavailable"]
                    : ["pointer", "kind", "role", "targetPath", "isNull", "package", "packageIndex", "exportIndex", "error"]);
                var pointer = String(reference, "pointer");
                var role = String(reference, "role");
                properties.ValidatePointer(pointer); String(reference, "kind");
                Require((pointer == "/Native/ClassDefaultObject") == (role == "class-default") &&
                    (pointer == "/Template") == (role == "template"), "Reference role differs from its native field.");
                if (unavailable.Reference(row, reference)) continue;
                Require(reference.GetProperty("error").ValueKind == JsonValueKind.Null, "Object reference failed to resolve.");
                var isNull = reference.GetProperty("isNull").GetBoolean();
                Require(isNull == (reference.GetProperty("targetPath").ValueKind == JsonValueKind.Null), "Inconsistent null reference.");
                if (!isNull)
                {
                    var target = ObjectPath(reference, "targetPath");
                    targets.Add(target);
                    if (role is "class-default" or "template") requiredBodies.Add(target);
                }
                NullableString(reference, "package");
                foreach (var field in new[] { "packageIndex", "exportIndex" })
                    if (reference.GetProperty(field).ValueKind != JsonValueKind.Null) reference.GetProperty(field).GetInt32();
            }
            foreach (var text in row.GetProperty("texts").EnumerateArray())
            {
                Fields(text, "pointer", "flags", "history", "namespace", "key", "source", "tableId");
                properties.ValidatePointer(String(text, "pointer")); text.GetProperty("flags").GetUInt32(); String(text, "history");
                foreach (var field in new[] { "namespace", "key", "source", "tableId" }) NullableString(text, field);
            }
            foreach (var value in row.GetProperty("values").EnumerateArray())
            {
                Fields(value, "pointer", "type", "kind", "value");
                properties.ValidatePointer(String(value, "pointer"));
                var type = String(value, "type");
                NullableString(value, "value");
                switch (String(value, "kind"))
                {
                    case "binary-base64": ValidateBinary(String(value, "value", allowEmpty: true)); break;
                    case "numeric-le-base64": ValidateNumeric(type, String(value, "value", allowEmpty: true)); break;
                }
            }
            foreach (var entry in row.GetProperty("tableEntries").EnumerateArray())
            {
                Fields(entry, "pointer", "namespace", "key", "source");
                properties.ValidatePointer(String(entry, "pointer"));
                foreach (var field in new[] { "namespace", "key", "source" }) String(entry, field, allowEmpty: true);
            }
        });
        Require(objects.Count > 0, "No object evidence.");
        return objects;
    }

    internal static IReadOnlyList<LocalizationEvidence> ReadLocalizations(IReadOnlyDictionary<string, SnapshotFile> files)
    {
        var locales = new List<LocalizationEvidence>();
        foreach (var (path, file) in files.Where(pair => SnapshotFiles.LocalePath.IsMatch(pair.Key)))
        {
            var entries = new Dictionary<(string Namespace, string Key), string>();
            JsonLines.Read(file, row =>
            {
                Fields(row, "namespace", "key", "value");
                Require(entries.TryAdd((String(row, "namespace", allowEmpty: true), String(row, "key", allowEmpty: true)),
                    String(row, "value", allowEmpty: true)), "Duplicate localization entry.");
            });
            Require(entries.Count > 0, "Empty localization dictionary.");
            locales.Add(new(path["localization/".Length..^".jsonl.gz".Length], entries));
        }
        return locales;
    }

    private static void ValidateNumeric(string type, string encoded)
    {
        var width = type switch
        {
            "Int8Property[]" => 1,
            "Int16Property[]" or "UInt16Property[]" => 2,
            "IntProperty[]" or "UInt32Property[]" or "FloatProperty[]" => 4,
            "Int64Property[]" or "UInt64Property[]" or "DoubleProperty[]" => 8,
            _ => 0
        };
        Require(width > 0, "Unknown encoded numeric array type.");
        Require(ValidateBinary(encoded) % width == 0, "Numeric array payload contains a partial element.");
    }

    private static int ValidateBinary(string encoded)
    {
        Require(encoded.Length % 4 == 0 && !encoded.Any(char.IsWhiteSpace) && Base64.IsValid(encoded), "Invalid binary base64 evidence.");
        if (encoded.Length == 0) return 0;
        // Re-encode the last quartet to reject nonzero padding bits. Earlier
        // quartets contain complete bytes; validation needs no payload allocation.
        Span<byte> bytes = stackalloc byte[3];
        Span<char> canonical = stackalloc char[4];
        var tail = encoded.AsSpan(encoded.Length - 4);
        Require(Convert.TryFromBase64Chars(tail, bytes, out var count) &&
            Convert.TryToBase64Chars(bytes[..count], canonical, out var written) &&
            tail.SequenceEqual(canonical[..written]), "Noncanonical binary base64 evidence.");
        return (encoded.Length / 4 - 1) * 3 + count;
    }

    internal static Dictionary<string, ResourceImage> ReadResources(IReadOnlyDictionary<string, SnapshotFile> files, Func<string, bool> objectExists)
    {
        using var document = ReadJson(files, "resources.json");
        var resources = new Dictionary<string, ResourceImage>(StringComparer.Ordinal);
        var images = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var row in document.RootElement.EnumerateArray())
        {
            Fields(row, "path", "status", "file", "width", "height");
            var path = ObjectPath(row, "path");
            Require(objectExists(path), "Image resource lacks object evidence.");
            Require(String(row, "status") == "exported", "Image resource failed to export.");
            var file = String(row, "file");
            Require(file == ResourceFiles.ImagePath(path) && images.Add(file), "Invalid or duplicate resource file.");
            var image = new ResourceImage(file, row.GetProperty("width").GetInt32(), row.GetProperty("height").GetInt32());
            Require(image.Width > 0 && image.Height > 0 && resources.TryAdd(path, image), "Invalid or duplicate image resource.");
            Require(files.TryGetValue(file, out var source), "Missing resource PNG.");
            ValidatePng(source!, image);
        }
        Require(images.SetEquals(files.Keys.Where(path => ResourceFiles.IsImagePath(path))), "PNG files and resource records do not match.");
        return resources;
    }

    private static void ValidatePng(SnapshotFile file, ResourceImage image)
    {
        using var stream = File.OpenRead(file.Path);
        Span<byte> signature = stackalloc byte[8];
        Require(stream.Read(signature) == 8 && signature.SequenceEqual(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }), "Invalid PNG signature.");
        stream.Position = 0;
        using var codec = SKCodec.Create(stream);
        Require(codec is not null && codec.Info.Width == image.Width && codec.Info.Height == image.Height, "PNG dimensions do not match.");
        using var pixels = new SKBitmap(codec!.Info);
        Require(codec.GetPixels(pixels.Info, pixels.GetPixels()) == SKCodecResult.Success, "PNG could not be fully decoded.");
    }

    internal static string ObjectPath(JsonElement value, string field)
    {
        var path = String(value, field);
        Require(path.StartsWith('/') && !path.Any(char.IsControl), $"Invalid object path: {field}.");
        return path;
    }

    private static void NullableString(JsonElement value, string field)
    {
        if (value.GetProperty(field).ValueKind != JsonValueKind.Null) String(value, field, allowEmpty: true);
    }
}
