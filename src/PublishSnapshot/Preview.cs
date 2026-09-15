using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using SkiaSharp;

namespace PublishSnapshot;

internal sealed record Preview(IReadOnlyDictionary<string, byte[]> Files)
{
    internal static readonly JsonSerializerOptions Json = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, WriteIndented = true };
    private static readonly Regex ImagePath = new("\\Aimages/[0-9a-f]{64}\\.png\\z", RegexOptions.CultureInvariant);

    public static Preview Read(string directory)
    {
        var files = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        ReadDirectory(Path.GetFullPath(directory), "", files);
        using var assets = JsonDocument.Parse(RequiredFile(files, "assets.json"));
        using var coverage = JsonDocument.Parse(RequiredFile(files, "coverage.json"));
        ValidateRecords(assets.RootElement, coverage.RootElement, files);
        return new(files);
    }

    private static void ReadDirectory(string directory, string prefix, Dictionary<string, byte[]> files)
    {
        Require((File.GetAttributes(directory) & FileAttributes.ReparsePoint) == 0, "Preview directories cannot be links.");
        foreach (var entry in Directory.EnumerateFileSystemEntries(directory))
        {
            var attributes = File.GetAttributes(entry);
            Require((attributes & FileAttributes.ReparsePoint) == 0, "Preview files cannot be links.");
            var relative = prefix + Path.GetFileName(entry);
            if ((attributes & FileAttributes.Directory) != 0)
            {
                Require(relative == "images", $"Unexpected preview directory: {relative}");
                ReadDirectory(entry, "images/", files);
                continue;
            }
            Require(relative is "assets.json" or "coverage.json" || ImagePath.IsMatch(relative), $"Unexpected preview file: {relative}");
            files.Add(relative, File.ReadAllBytes(entry));
        }
    }

    private static void ValidateRecords(JsonElement assets, JsonElement report, Dictionary<string, byte[]> files)
    {
        Fields(report, "status", "registeredAssets", "candidates", "loaded", "assetIds", "englishNames", "descriptions", "images", "issues");
        Require(report.GetProperty("status").GetString() == "succeeded", "Preview is incomplete.");
        Require(report.GetProperty("issues").ValueKind == JsonValueKind.Array && report.GetProperty("issues").GetArrayLength() == 0, "Preview has diagnostics.");
        Require(assets.ValueKind == JsonValueKind.Array && assets.GetArrayLength() > 0, "Preview has no asset records.");
        var ids = new HashSet<string>(StringComparer.Ordinal);
        var referenced = new Dictionary<string, (int Width, int Height)>(StringComparer.Ordinal);
        var names = 0;
        var descriptions = 0;
        var illustrated = 0;
        foreach (var asset in assets.EnumerateArray())
        {
            Fields(asset, "id", "definitions", "metadata", "text", "images");
            var id = String(asset, "id");
            Require(long.TryParse(id, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var numeric) && numeric != 0 && numeric.ToString(CultureInfo.InvariantCulture) == id, "Asset ID must be a canonical nonzero signed 64-bit string.");
            Require(ids.Add(id), $"Duplicate asset ID: {id}");
            Require(asset.GetProperty("definitions").GetArrayLength() > 0, $"Asset {id} has no publishable definition.");
            var sources = new HashSet<string>(StringComparer.Ordinal);
            ValidateSources(asset.GetProperty("definitions"), sources);
            ValidateSources(asset.GetProperty("metadata"), sources);
            Require(sources.Count > 0, $"Asset {id} has no sources.");
            var english = ValidateTranslations(asset.GetProperty("text"));
            if (english.Name) names++;
            if (english.Description) descriptions++;
            if (ValidateImages(asset.GetProperty("images"), sources, referenced)) illustrated++;
        }
        CheckCount(report, "assetIds", ids.Count);
        CheckCount(report, "englishNames", names);
        CheckCount(report, "descriptions", descriptions);
        CheckCount(report, "images", illustrated);
        var candidates = report.GetProperty("candidates").GetInt32();
        Require(candidates > 0 && report.GetProperty("registeredAssets").GetInt32() > 0, "Discovery counts must be positive.");
        CheckCount(report, "loaded", candidates);
        Require(files.Keys.Where(path => ImagePath.IsMatch(path)).ToHashSet(StringComparer.Ordinal).SetEquals(referenced.Keys), "PNG files and exported references do not match.");
        foreach (var (path, dimensions) in referenced) ValidatePng(path, RequiredFile(files, path), dimensions);
    }

    private static (bool Name, bool Description) ValidateTranslations(JsonElement text)
    {
        var locales = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var english = (Name: false, Description: false);
        foreach (var translation in text.EnumerateArray())
        {
            Fields(translation, "locale", "displayName", "description");
            var locale = String(translation, "locale");
            Require(locales.Add(locale), $"Duplicate locale: {locale}");
            var name = String(translation, "displayName", allowEmpty: true);
            var description = String(translation, "description", allowEmpty: true);
            if (locale == "en") english = (name.Length > 0, description.Length > 0);
        }
        return english;
    }

    private static void ValidateSources(JsonElement sources, HashSet<string> paths)
    {
        foreach (var source in sources.EnumerateArray())
        {
            Fields(source, "name", "class", "path");
            Require(!string.IsNullOrWhiteSpace(String(source, "name")) && !string.IsNullOrWhiteSpace(String(source, "class")), "Source names and classes must not be blank.");
            var path = String(source, "path");
            Require(path.StartsWith('/') && !path.Any(char.IsControl) && paths.Add(path), "Invalid or duplicate source path.");
        }
    }

    private static bool ValidateImages(JsonElement images, HashSet<string> objectPaths, Dictionary<string, (int Width, int Height)> referenced)
    {
        var sources = new HashSet<(string, string)>();
        var exported = false;
        foreach (var image in images.EnumerateArray())
        {
            Fields(image, "field", "source", "texture", "status", "file", "width", "height");
            var source = String(image, "source");
            Require(objectPaths.Contains(source) && sources.Add((source, String(image, "field"))), "Invalid or duplicate image source/field.");
            var status = String(image, "status");
            if (status == "absent")
            {
                Require(new[] { "texture", "file", "width", "height" }.All(field => image.GetProperty(field).ValueKind == JsonValueKind.Null), "Absent image has export values.");
                continue;
            }
            Require(status == "exported", "Preview contains a failed or unsupported image.");
            var texture = String(image, "texture");
            var path = String(image, "file");
            Require(ImagePath.IsMatch(path), "Image path must be images/<64 lowercase hex>.png.");
            var textureHash = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(texture)));
            Require(path == $"images/{textureHash}.png", "Image filename does not match its declared texture path.");
            var size = (Width: image.GetProperty("width").GetInt32(), Height: image.GetProperty("height").GetInt32());
            Require(size.Width > 0 && size.Height > 0, "Image dimensions must be positive.");
            Require(!referenced.TryGetValue(path, out var prior) || prior == size, "Conflicting dimensions for shared PNG.");
            referenced[path] = size;
            exported = true;
        }
        return exported;
    }

    private static void ValidatePng(string path, byte[] bytes, (int Width, int Height) size)
    {
        Require(bytes.AsSpan().StartsWith(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }), $"Invalid PNG signature: {path}");
        using var codec = SKCodec.Create(new SKMemoryStream(bytes));
        Require(codec is not null && codec.Info.Width == size.Width && codec.Info.Height == size.Height, $"PNG dimensions do not match: {path}");
        using var pixels = new SKBitmap(codec!.Info);
        Require(codec.GetPixels(pixels.Info, pixels.GetPixels()) == SKCodecResult.Success, $"PNG could not be fully decoded: {path}");
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

    private static byte[] RequiredFile(IReadOnlyDictionary<string, byte[]> files, string path) =>
        files.TryGetValue(path, out var bytes) ? bytes : throw new InvalidDataException($"Missing preview file: {path}");

    private static void CheckCount(JsonElement report, string field, int actual) => Require(report.GetProperty(field).GetInt32() == actual, $"Coverage count mismatch: {field}.");
}
