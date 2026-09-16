using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace PublishSnapshot;

internal sealed record Preview(SnapshotFiles Snapshot) : IDisposable
{
    internal static readonly JsonSerializerOptions Json = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, WriteIndented = true };
    private static readonly Regex ImagePath = SnapshotFiles.ImagePath;
    public IReadOnlyDictionary<string, SnapshotFile> Files => Snapshot.Files;
    public void Dispose() => Snapshot.Dispose();

    public static Preview Read(string directory)
    {
        var snapshot = SnapshotFiles.Capture(directory);
        try
        {
            using var assets = ReadJson(snapshot.Files, "assets.json");
            using var coverage = ReadJson(snapshot.Files, "coverage.json");
            Fields(coverage.RootElement, "status", "registeredAssets", "candidates", "loaded", "assetIds", "englishNames", "descriptions", "images", "issues", "notices", "discovery");
            Require(coverage.RootElement.GetProperty("status").GetString() == "succeeded", "Preview is incomplete.");
            Require(coverage.RootElement.GetProperty("issues").GetArrayLength() == 0, "Preview has diagnostics.");
            var evidence = ResourceEvidence.Read(snapshot.Files, coverage.RootElement);
            ValidateRecords(assets.RootElement, coverage.RootElement, evidence);
            TextOriginChecks.Validate(assets.RootElement, snapshot.Files["discovery/objects.jsonl.gz"]);
            SemanticChecks.Validate(assets.RootElement, snapshot.Files, coverage.RootElement);
            return new(snapshot);
        }
        catch { snapshot.Dispose(); throw; }
    }

    internal static JsonDocument ReadJson(IReadOnlyDictionary<string, SnapshotFile> files, string path)
    {
        using var stream = File.OpenRead(files[path].Path);
        return JsonDocument.Parse(stream);
    }

    private static void ValidateRecords(JsonElement assets, JsonElement report, ResourceEvidence evidence)
    {
        Require(assets.ValueKind == JsonValueKind.Array && assets.GetArrayLength() > 0, "Preview has no asset records.");
        var ids = new HashSet<string>(StringComparer.Ordinal);
        var names = 0;
        var descriptions = 0;
        var illustrated = 0;
        foreach (var asset in assets.EnumerateArray())
        {
            Fields(asset, "id", "definitions", "metadata", "text", "images", "presentation");
            var id = String(asset, "id");
            Require(long.TryParse(id, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var numeric) && numeric != 0 && numeric.ToString(CultureInfo.InvariantCulture) == id, "Asset ID must be a canonical nonzero signed 64-bit string.");
            Require(ids.Add(id), $"Duplicate asset ID: {id}");
            Require(asset.GetProperty("definitions").GetArrayLength() > 0, $"Asset {id} has no publishable definition.");
            var sources = new HashSet<string>(StringComparer.Ordinal);
            ValidateSources(asset.GetProperty("definitions"), sources, evidence.ObjectPaths);
            ValidateSources(asset.GetProperty("metadata"), sources, evidence.ObjectPaths);
            Require(sources.Count > 0, $"Asset {id} has no sources.");
            PresentationChecks.Validate(asset, sources, evidence.ObjectPaths);
            var english = LocalizedTextChecks.Validate(asset.GetProperty("text"), evidence.Localizations, asset.GetProperty("presentation"));
            if (english.Name) names++;
            if (english.Description) descriptions++;
            if (ValidateImages(asset.GetProperty("images"), sources, evidence.Resources)) illustrated++;
        }
        CheckCount(report, "assetIds", ids.Count);
        CheckCount(report, "englishNames", names);
        CheckCount(report, "descriptions", descriptions);
        CheckCount(report, "images", illustrated);
        var candidates = report.GetProperty("candidates").GetInt32();
        Require(candidates > 0 && report.GetProperty("registeredAssets").GetInt32() > 0, "Discovery counts must be positive.");
        CheckCount(report, "loaded", candidates);
    }

    private static void ValidateSources(JsonElement sources, HashSet<string> paths, HashSet<string> discovered)
    {
        foreach (var source in sources.EnumerateArray())
        {
            Fields(source, "name", "class", "path");
            Require(!string.IsNullOrWhiteSpace(String(source, "name")) && !string.IsNullOrWhiteSpace(String(source, "class")), "Source names and classes must not be blank.");
            var path = String(source, "path");
            Require(path.StartsWith('/') && !path.Any(char.IsControl) && paths.Add(path) && discovered.Contains(path), "Invalid or duplicate source path.");
        }
    }

    private static bool ValidateImages(JsonElement images, HashSet<string> objectPaths, IReadOnlyDictionary<string, ResourceImage> resources)
    {
        var sources = new HashSet<(string, string)>();
        var exported = false;
        foreach (var image in images.EnumerateArray())
        {
            Fields(image, "field", "source", "resource", "status", "file", "width", "height");
            var source = String(image, "source");
            Require(objectPaths.Contains(source) && sources.Add((source, String(image, "field"))), "Invalid or duplicate image source/field.");
            var status = String(image, "status");
            if (status == "absent")
            {
                Require(new[] { "resource", "file", "width", "height" }.All(field => image.GetProperty(field).ValueKind == JsonValueKind.Null), "Absent image has export values.");
                continue;
            }
            Require(status == "exported", "Preview contains a failed or unsupported image.");
            var resourcePath = String(image, "resource");
            var path = String(image, "file");
            Require(ImagePath.IsMatch(path), "Image path must be images/<64 lowercase hex>.png.");
            var resourceHash = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(resourcePath)));
            Require(path == $"images/{resourceHash}.png", "Image filename does not match its declared resource path.");
            var size = (Width: image.GetProperty("width").GetInt32(), Height: image.GetProperty("height").GetInt32());
            Require(size.Width > 0 && size.Height > 0, "Image dimensions must be positive.");
            Require(resources.TryGetValue(resourcePath, out var resource) && resource.File == path
                && resource.Width == size.Width && resource.Height == size.Height, "Image does not match its resource record.");
            exported = true;
        }
        return exported;
    }

    internal static void Fields(JsonElement value, params string[] fields)
    {
        Require(value.ValueKind == JsonValueKind.Object, "Expected a JSON object.");
        var found = new HashSet<string>(StringComparer.Ordinal);
        foreach (var property in value.EnumerateObject()) Require(found.Add(property.Name), $"Duplicate JSON field: {property.Name}");
        Require(found.SetEquals(fields), "JSON fields do not match format version 1.");
    }

    internal static string String(JsonElement value, string name, bool allowEmpty = false)
    {
        var field = value.GetProperty(name);
        Require(field.ValueKind == JsonValueKind.String, $"Expected string field: {name}");
        var text = field.GetString()!;
        Require(allowEmpty || text.Length > 0, $"Empty string field: {name}");
        return text;
    }

    internal static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidDataException(message);
    }

    internal static void CheckCount(JsonElement report, string field, int actual) => Require(report.GetProperty(field).GetInt32() == actual, $"Coverage count mismatch: {field}.");
}
