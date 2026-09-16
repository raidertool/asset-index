using CUE4Parse.MappingsProvider;
using CUE4Parse.UE4.Assets.Exports;
using CUE4Parse.UE4.Assets.Objects;
using CUE4Parse.UE4.Assets.Objects.Properties;
using CUE4Parse_Conversion.Options;
using CUE4Parse_Conversion.Textures;

namespace AssetIndex;

internal sealed record AssetImage(string Field, string Source, string? Resource, string Status,
    string? File = null, int? Width = null, int? Height = null);

internal sealed record ImageRequest(string Field, string Source, string? Resource, string Status,
    ObjectLocation? Location = null);

internal static class Images
{
    public static IReadOnlyList<ImageRequest> Capture(UObject source, TypeMappings mappings, Func<UObject, ObjectLocation> locate,
        ICollection<ExtractionIssue> issues)
    {
        var images = new List<ImageRequest>();
        var path = ObjectMetadata.Path(source);
        foreach (var requestedField in ImageFields.Simple.Concat(ImageFields.Typed.Keys))
        {
            var field = requestedField;
            try
            {
                if (Properties.Find(source, field) is not { } property) continue;
                field = property.Name.Text;
                if (!SupportsField(source, mappings, requestedField, property)) continue;
                images.Add(CaptureReference(property, field, path, locate, issues));
            }
            catch (Exception error)
            {
                images.Add(Failed(field, path, null, error, issues));
            }
        }
        MapImages.Capture(source, mappings, locate, images, issues);
        return images;
    }

    internal static ImageRequest CaptureReference(FPropertyTag property, string field, string path,
        Func<UObject, ObjectLocation> locate, ICollection<ExtractionIssue> issues)
    {
        string? resourcePath = null;
        try
        {
            var reference = Properties.Reference(property, $"{path}.{field}");
            if (reference is null) return new(field, path, null, "absent");
            resourcePath = ObjectMetadata.Path(reference);
            return new(field, path, resourcePath, "pending", locate(reference));
        }
        catch (Exception error) { return Failed(field, path, resourcePath, error, issues); }
    }

    internal static ImageRequest Failed(string field, string path, string? resourcePath, Exception error,
        ICollection<ExtractionIssue> issues)
    {
        issues.Add(new("image", $"{path}.{field}", error.Message));
        return new(field, path, resourcePath, "failed");
    }

    private static bool SupportsField(UObject source, TypeMappings mappings, string field, FPropertyTag property)
    {
        if (!ImageFields.Typed.TryGetValue(field, out var owner)) return true;
        var schema = ClassSchema.Read(source, mappings);
        if (!schema.IsA(owner)) return false;
        if (!schema.HasProperty(field, "SoftObjectProperty") || property.Tag is not SoftObjectProperty)
            throw new InvalidDataException($"Image field {ObjectMetadata.Path(source)}.{property.Name.Text} is not a declared soft object reference.");
        return true;
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
