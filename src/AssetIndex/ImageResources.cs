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
    public IReadOnlyCollection<ImageResource> Entries => resources.Values;

    public ImageResource Export(UObject source)
    {
        var path = source.GetPathName();
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
