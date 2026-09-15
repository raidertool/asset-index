using System.Security.Cryptography;
using System.Text;
using CUE4Parse.UE4.Assets.Exports.Texture;
using CUE4Parse_Conversion.Options;
using CUE4Parse_Conversion.Textures;

namespace AssetIndex;

internal sealed record AssetImage(string Field, string Source, string? Texture, string Status,
    string? File = null, int? Width = null, int? Height = null);

internal static class Images
{
    private static readonly string[] Fields =
    [
        "Icon", "BigIcon", "TinyIcon", "Image", "CurrencyIcon", "CurrencyBigIcon", "EnemyIcon", "EnemyImage",
        "OfferImage", "OfferImageSquare", "OfferImageWide", "OfferImagePortrait"
    ];

    public static IReadOnlyList<AssetImage> Export(CatalogAsset asset, string output, ICollection<ExtractionIssue> issues)
    {
        var images = new List<AssetImage>();
        foreach (var source in asset.Metadata.Prepend(asset.Definition))
        {
            foreach (var field in Fields)
            {
                var path = source.GetPathName();
                try
                {
                    if (Properties.Find(source, field) is null) continue;
                    var reference = Properties.Reference(source, field);
                    if (reference is null)
                    {
                        images.Add(new(field, path, null, "absent"));
                        continue;
                    }
                    if (reference is not UTexture2D texture)
                        throw new InvalidDataException($"Expected Texture2D, found {reference.ExportType}.");
                    var texturePath = texture.GetPathName();
                    var hash = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(texturePath)));
                    var file = $"images/{hash}.png";
                    var decoded = texture.Decode()
                        ?? throw new InvalidDataException("Texture has no decodable mip.");
                    Directory.CreateDirectory(Path.Combine(output, "images"));
                    System.IO.File.WriteAllBytes(Path.Combine(output, file), Encode(decoded));
                    images.Add(new(field, path, texturePath, "exported", file, decoded.Width, decoded.Height));
                }
                catch (Exception error)
                {
                    issues.Add(new("image", $"{path}.{field}", error.Message));
                    images.Add(new(field, path, null, "failed"));
                }
            }
        }
        return images;
    }

    internal static byte[] Encode(CTexture texture)
    {
        if (texture.Width <= 0 || texture.Height <= 0)
            throw new InvalidDataException("Texture dimensions must be positive.");
        return texture.Encode(ETextureFormat.Png, false, out _);
    }
}
