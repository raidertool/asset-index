using System.Security.Cryptography;
using System.Text;
using CUE4Parse.UE4.Assets.Exports.Texture;
using CUE4Parse_Conversion.Textures;

namespace AssetIndex;

internal sealed record TextureResource(string Path, string Status, string? File = null, int? Width = null, int? Height = null);

internal sealed class TextureResources(string output, ICollection<ExtractionIssue> issues)
{
    private readonly SortedDictionary<string, TextureResource> resources = new(StringComparer.Ordinal);
    public IReadOnlyCollection<TextureResource> Entries => resources.Values;

    public TextureResource Export(UTexture2D texture)
    {
        var path = texture.GetPathName();
        if (resources.TryGetValue(path, out var existing)) return existing;
        TextureResource result;
        try
        {
            var decoded = texture.Decode() ?? throw new InvalidDataException("Texture has no decodable mip.");
            var hash = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(path)));
            var file = $"images/{hash}.png";
            var bytes = Images.Encode(decoded);
            Snapshot.WriteFile(output, file, stream => stream.Write(bytes));
            result = new(path, "exported", file, decoded.Width, decoded.Height);
        }
        catch (Exception error)
        {
            issues.Add(new("texture", path, error.Message));
            result = new(path, "failed");
        }
        resources.Add(path, result);
        return result;
    }
}
