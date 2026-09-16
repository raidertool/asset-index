using CUE4Parse.UE4.Assets.Exports;
using CUE4Parse_Conversion.Options;
using CUE4Parse_Conversion.Textures;

namespace AssetIndex;

internal sealed record AssetImage(string Field, string Source, string? Resource, string Status,
    string? File = null, int? Width = null, int? Height = null);

internal sealed record ImageRequest(string Field, string Source, string? Resource, string Status,
    ObjectLocation? Location = null);

internal static class Images
{
    private static readonly string[] Fields =
    [
        "Icon", "BigIcon", "TinyIcon", "Image", "CurrencyIcon", "CurrencyBigIcon", "EnemyIcon", "EnemyImage",
        "OfferImage", "OfferImage_1x1", "OfferImage_2x1", "OfferImage_9x16", "OfferImage_16x9", "OfferImage_Thumbnail",
        "BigImage", "CollapsedImage", "BattlepassListImage", "BattlepassCoverImage", "LocationPreviewImage",
        "Portrait", "ImageAsset", "UnlockImage", "PreviewImage", "IconMaterial", "EmptySlotImage",
        "ModifierIcon", "ObscuredPreviewImage", "OptionalLocationIcon", "UnlockVideoPreviewImage"
    ];

    public static IReadOnlyList<ImageRequest> Capture(UObject source, Func<UObject, ObjectLocation> locate,
        ICollection<ExtractionIssue> issues)
    {
        var images = new List<ImageRequest>();
        var path = ObjectMetadata.Path(source);
        foreach (var requestedField in Fields)
        {
            var field = requestedField;
            string? resourcePath = null;
            try
            {
                if (Properties.Find(source, field) is not { } property) continue;
                field = property.Name.Text;
                var reference = Properties.Reference(source, field);
                if (reference is null)
                {
                    images.Add(new(field, path, null, "absent"));
                    continue;
                }
                resourcePath = ObjectMetadata.Path(reference);
                images.Add(new(field, path, resourcePath, "pending", locate(reference)));
            }
            catch (Exception error)
            {
                issues.Add(new("image", $"{path}.{field}", error.Message));
                images.Add(new(field, path, resourcePath, "failed"));
            }
        }
        return images;
    }

    public static IReadOnlyList<AssetImage> Export(CatalogAsset asset, ImageResources resources,
        Func<ObjectLocation, UObject> load, ICollection<ExtractionIssue> issues)
    {
        var images = new List<AssetImage>();
        foreach (var request in asset.Definitions.Concat(asset.Metadata).SelectMany(source => source.Images))
        {
            if (request.Status != "pending")
            {
                images.Add(new(request.Field, request.Source, request.Resource, request.Status));
                continue;
            }
            try
            {
                var location = request.Location ?? throw new InvalidDataException("Image request has no export location.");
                var resource = resources.Export(location, load);
                images.Add(new(request.Field, request.Source, resource.Path, resource.Status,
                    resource.File, resource.Width, resource.Height));
            }
            catch (Exception error)
            {
                issues.Add(new("image", $"{request.Source}.{request.Field}", error.Message));
                images.Add(new(request.Field, request.Source, request.Resource, "failed"));
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
