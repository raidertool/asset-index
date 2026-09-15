using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using SkiaSharp;
using static PublishSnapshot.Preview;

namespace PublishSnapshot;

internal sealed record ResourceImage(string File, int Width, int Height);
internal sealed record ResourceEvidence(HashSet<string> ObjectPaths, HashSet<string> Locales,
    IReadOnlyDictionary<string, ResourceImage> Resources)
{
    public static ResourceEvidence Read(IReadOnlyDictionary<string, SnapshotFile> files, JsonElement report)
    {
        var discovery = report.GetProperty("discovery");
        Fields(discovery, "nativeScope", "mappingSha256", "objects", "resources");
        String(discovery, "nativeScope");
        Require(Regex.IsMatch(String(discovery, "mappingSha256"), "\\A[0-9a-f]{64}\\z"), "Invalid mapping hash.");
        foreach (var notice in report.GetProperty("notices").EnumerateArray())
        {
            Fields(notice, "stage", "path", "message");
            foreach (var field in new[] { "stage", "path", "message" }) String(notice, field);
        }
        var objects = ReadObjects(files["discovery/objects.jsonl.gz"]);
        CheckCount(discovery, "objects", objects.Count);
        var registry = new HashSet<string>(StringComparer.Ordinal);
        var registryPackages = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        JsonLines.Read(files["discovery/registry.jsonl.gz"], row =>
        {
            Fields(row, "path", "package", "class", "tags");
            Require(registry.Add(ObjectPath(row, "path")), "Duplicate registry object.");
            registryPackages.Add(ObjectPath(row, "package"));
            String(row, "class");
            var tags = new HashSet<string>(StringComparer.Ordinal);
            foreach (var tag in row.GetProperty("tags").EnumerateObject())
                Require(tags.Add(tag.Name) && tag.Value.ValueKind == JsonValueKind.String, "Invalid registry tag.");
        });
        CheckCount(report, "registeredAssets", registry.Count);
        var inputs = MountedInputs.Read(files["discovery/files.jsonl.gz"], registryPackages);
        var packages = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        long exportsTotal = 0;
        JsonLines.Read(files["discovery/packages.jsonl.gz"], row =>
        {
            Fields(row, "path", "reason", "status", "exports", "loaded");
            var path = String(row, "path");
            Require(inputs.Paths.Contains(path), $"Discovered package is absent from mounted inputs: {path}.");
            Require(packages.Add(path), "Duplicate discovered package.");
            String(row, "reason");
            var exports = row.GetProperty("exports").GetInt32();
            Require(String(row, "status") == "loaded" && exports >= 0 && row.GetProperty("loaded").GetInt32() == exports,
                "Discovery contains an incomplete package.");
            exportsTotal += exports;
        });
        CheckCount(report, "candidates", packages.Count);
        Require(exportsTotal == objects.Count, "Discovered export and object counts differ.");
        Require(inputs.Unindexed.IsSubsetOf(packages), "An unindexed mounted package was not discovered.");
        var locales = ReadLocalizations(files);
        var resources = ReadResources(files, objects);
        CheckCount(discovery, "resources", resources.Count);
        return new(objects, locales, resources);
    }

    private static HashSet<string> ReadObjects(SnapshotFile file)
    {
        var objects = new HashSet<string>(StringComparer.Ordinal);
        var targets = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        JsonLines.Read(file, row =>
        {
            Fields(row, "path", "class", "properties", "references", "texts", "values", "tableEntries", "issues");
            Require(objects.Add(ObjectPath(row, "path")), "Duplicate discovered object.");
            String(row, "class");
            Require(row.GetProperty("issues").GetArrayLength() == 0, "Object evidence contains diagnostics.");
            var properties = PropertyHeaders.Read(row.GetProperty("properties"));
            foreach (var reference in row.GetProperty("references").EnumerateArray())
            {
                Fields(reference, "pointer", "kind", "role", "targetPath", "isNull", "package", "packageIndex", "exportIndex", "error");
                properties.ValidatePointer(String(reference, "pointer")); String(reference, "kind"); String(reference, "role");
                Require(reference.GetProperty("error").ValueKind == JsonValueKind.Null, "Object reference failed to resolve.");
                var isNull = reference.GetProperty("isNull").GetBoolean();
                Require(isNull == (reference.GetProperty("targetPath").ValueKind == JsonValueKind.Null), "Inconsistent null reference.");
                if (!isNull) targets.Add(ObjectPath(reference, "targetPath"));
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
                properties.ValidatePointer(String(value, "pointer")); String(value, "type"); String(value, "kind"); NullableString(value, "value");
            }
            foreach (var entry in row.GetProperty("tableEntries").EnumerateArray())
            {
                Fields(entry, "pointer", "namespace", "key", "source");
                properties.ValidatePointer(String(entry, "pointer"));
                foreach (var field in new[] { "namespace", "key", "source" }) String(entry, field, allowEmpty: true);
            }
        });
        Require(objects.Count > 0, "No object evidence.");
        ReferenceClosure.Validate(objects, targets);
        return objects;
    }

    private static HashSet<string> ReadLocalizations(IReadOnlyDictionary<string, SnapshotFile> files)
    {
        var locales = new HashSet<string>(StringComparer.Ordinal);
        foreach (var (path, file) in files.Where(pair => SnapshotFiles.LocalePath.IsMatch(pair.Key)))
        {
            var entries = new HashSet<(string, string)>();
            JsonLines.Read(file, row =>
            {
                Fields(row, "namespace", "key", "value");
                Require(entries.Add((String(row, "namespace", allowEmpty: true), String(row, "key", allowEmpty: true))), "Duplicate localization entry.");
                String(row, "value", allowEmpty: true);
            });
            Require(entries.Count > 0, "Empty localization dictionary.");
            locales.Add(path["localization/".Length..^".jsonl.gz".Length]);
        }
        return locales;
    }

    private static Dictionary<string, ResourceImage> ReadResources(IReadOnlyDictionary<string, SnapshotFile> files, HashSet<string> objects)
    {
        using var document = ReadJson(files, "resources.json");
        var resources = new Dictionary<string, ResourceImage>(StringComparer.Ordinal);
        var images = new HashSet<string>(StringComparer.Ordinal);
        foreach (var row in document.RootElement.EnumerateArray())
        {
            Fields(row, "path", "status", "file", "width", "height");
            var path = ObjectPath(row, "path");
            Require(objects.Contains(path), "Image resource lacks object evidence.");
            Require(String(row, "status") == "exported", "Image resource failed to export.");
            var file = String(row, "file");
            var hash = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(path)));
            Require(file == $"images/{hash}.png" && images.Add(file), "Invalid or duplicate resource file.");
            var image = new ResourceImage(file, row.GetProperty("width").GetInt32(), row.GetProperty("height").GetInt32());
            Require(image.Width > 0 && image.Height > 0 && resources.TryAdd(path, image), "Invalid or duplicate image resource.");
            Require(files.TryGetValue(file, out var source), "Missing resource PNG.");
            ValidatePng(source!, image);
        }
        Require(images.SetEquals(files.Keys.Where(path => SnapshotFiles.ImagePath.IsMatch(path))), "PNG files and resource records do not match.");
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
