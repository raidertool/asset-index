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
        "OfferImage", "OfferImage_1x1", "OfferImage_2x1", "OfferImage_9x16", "OfferImage_16x9", "OfferImage_Thumbnail",
        "BigImage", "CollapsedImage", "BattlepassListImage", "BattlepassCoverImage", "LocationPreviewImage",
        "Portrait", "ImageAsset", "UnlockImage", "PreviewImage", "IconMaterial", "EmptySlotImage"
    ];

    public static IReadOnlyList<AssetImage> Export(CatalogAsset asset, TextureResources resources, ICollection<ExtractionIssue> issues)
    {
        var images = new List<AssetImage>();
        foreach (var source in asset.Definitions.Concat(asset.Metadata))
        {
            foreach (var field in Fields)
            {
                var path = source.GetPathName();
                string? texturePath = null;
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
                    {
                        issues.Add(new("image", $"{path}.{field}", $"Unsupported image source: {reference.ExportType}."));
                        images.Add(new(field, path, reference.GetPathName(), "unsupported"));
                        continue;
                    }
                    texturePath = texture.GetPathName();
                    var resource = resources.Export(texture);
                    images.Add(new(field, path, texturePath, resource.Status, resource.File, resource.Width, resource.Height));
                }
                catch (Exception error)
                {
                    issues.Add(new("image", $"{path}.{field}", error.Message));
                    images.Add(new(field, path, texturePath, "failed"));
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
