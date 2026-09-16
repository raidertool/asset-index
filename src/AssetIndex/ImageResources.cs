using System.Security.Cryptography;
using System.Text;
using CUE4Parse.UE4.Assets.Exports;
using CUE4Parse.UE4.Assets.Exports.Material;
using CUE4Parse.UE4.Assets.Exports.Texture;
using CUE4Parse_Conversion.Textures;

namespace AssetIndex;

internal sealed record ImageResource(string Path, string Status, string? File = null, int? Width = null, int? Height = null);

internal sealed class ImageResources(string output, ICollection<ExtractionIssue> issues, MaterialIcons? materials = null)
{
    private readonly SortedDictionary<string, ImageResource> resources = new(StringComparer.Ordinal);
    private readonly Dictionary<string, ObjectLocation> locations = new(StringComparer.OrdinalIgnoreCase);
    public IReadOnlyCollection<ImageResource> Entries => resources.Values;

    public ImageResource Export(ObjectLocation location, Func<ObjectLocation, UObject> load)
    {
        if (locations.TryGetValue(location.Path, out var original))
        {
            if (ReferenceEquals(original.File, location.File) && original.ExportIndex == location.ExportIndex)
                return resources[original.Path];
            issues.Add(new("image", location.Path, "Image path refers to conflicting physical exports."));
            return new(location.Path, "failed");
        }
        locations.Add(location.Path, location);
        if (resources.TryGetValue(location.Path, out var existing)) return existing;
        UObject source;
        try
        {
            source = load(location);
            if (!ObjectMetadata.Path(source).Equals(location.Path, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException($"Reloaded image does not match {location.Path}.");
        }
        catch (Exception error)
        {
            issues.Add(new("image", location.Path, error.Message));
            var failed = new ImageResource(location.Path, "failed");
            resources.Add(location.Path, failed);
            return failed;
        }
        return Export(source, location.Path);
    }

    public ImageResource Export(UObject source) => Export(source, ObjectMetadata.Path(source));

    private ImageResource Export(UObject source, string path)
    {
        if (resources.TryGetValue(path, out var existing)) return existing;
        ImageResource result;
        try
        {
            var decoded = source switch
            {
                UTexture2D texture => texture.Decode() ?? throw new InvalidDataException("Texture has no decodable mip."),
                UMaterialInstanceConstant material when materials is not null => materials.Render(material),
                UMaterial material when materials is not null => materials.Render(material),
                _ => throw new NotSupportedException($"Unsupported image source: {source.ExportType}.")
            };
            var hash = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(path)));
            var file = $"images/{hash}.png";
            var bytes = Images.Encode(decoded);
            Snapshot.WriteFile(output, file, stream => stream.Write(bytes));
            result = new(path, "exported", file, decoded.Width, decoded.Height);
        }
        catch (NotSupportedException error)
        {
            issues.Add(new("image", path, error.Message));
            result = new(path, "unsupported");
        }
        catch (Exception error)
        {
            issues.Add(new("image", path, error.Message));
            result = new(path, "failed");
        }
        resources.Add(path, result);
        return result;
    }
}
