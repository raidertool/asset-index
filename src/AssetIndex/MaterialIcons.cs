using System.Numerics;
using System.Security.Cryptography;
using CUE4Parse.GameTypes.Theia.FileProvider;
using CUE4Parse.UE4.Assets.Exports.Material;
using CUE4Parse.UE4.Assets.Exports.Texture;
using CUE4Parse.UE4.Assets.Objects;
using CUE4Parse.UE4.Objects.Engine;
using CUE4Parse_Conversion.Textures;

namespace AssetIndex;

internal sealed record MaterialSampling(TextureAddress X, TextureAddress Y, TextureFilter Filter);
internal sealed record IconParameters(Vector3 ColorA, Vector3 ColorB, Vector3 ColorC,
    float MetalA = 0, float MetalB = 0, float MetalC = 0, float CoverageB = 0, float CoverageC = 0,
    Vector4 Selection = default);

// Verified material graphs rendered as standalone, unscaled UI images with
// white tint, no widget effects, sRGB output and straight alpha.
internal sealed class MaterialIcons(TheiaFileProvider provider, MaterialSampling backgroundSampling, MaterialSampling bevelSampling)
{
    internal const string ParentPath = "/Game/Pioneer/UI/Assets/Materials/M_CharacterColorSchemeIcon.M_CharacterColorSchemeIcon";
    internal const string ParentHash = "db3d344eae234463df74e485ff0710460e17b40d603fb8fe87bfedfa462a13d6";
    internal const string BackgroundPath = "/Game/Pioneer/UI/Assets/Materials/T_CharacterColorSchemeIcon_Background.T_CharacterColorSchemeIcon_Background";
    internal const string BevelPath = "/Game/Pioneer/UI/Assets/Materials/T_CharacterColorSchemeIcon_Bevel.T_CharacterColorSchemeIcon_Bevel";
    private readonly ExtractorIcon extractor = new(provider);
    private MaterialTexture? background;
    private MaterialTexture? bevel;

    public CTexture Render(UMaterial material) => extractor.Render(material);

    public CTexture Render(UMaterialInstanceConstant material)
    {
        if (material.Parent is not UMaterial parent || parent.GetPathName() != ParentPath)
            throw new NotSupportedException("Material does not use the verified color icon parent.");
        var parameters = ReadParameters(material);
        if (background is null || bevel is null)
        {
            var path = GameFiles.ResolvePackagePath(provider, ParentPath[..ParentPath.LastIndexOf('.')]);
            VerifyParent(provider[path].Read());
            if (parent.CachedExpressionData is null || !parent.CachedExpressionData.TryGetValue<UTexture2D[]>(out var inputs, "ReferencedTextures") || inputs.Length != 2)
                throw new NotSupportedException("Color icon parent texture references changed.");
            var first = inputs[0];
            var second = inputs[1];
            background = ReadTexture(first, BackgroundPath, 256, 256, true, backgroundSampling);
            bevel = ReadTexture(second, BevelPath, 128, 8, false, bevelSampling);
        }
        return RenderPixels(parameters, background, bevel);
    }

    internal static void VerifyParent(byte[] package)
    {
        if (Convert.ToHexStringLower(SHA256.HashData(package)) != ParentHash)
            throw new NotSupportedException("Color icon parent package differs from the verified shader graph.");
    }

    private static MaterialTexture ReadTexture(UTexture2D? texture, string path, int width, int height, bool srgb, MaterialSampling sampling)
    {
        if (texture is null || texture.GetPathName() != path || texture.SRGB != srgb || texture.IsNormalMap ||
            texture.AddressX != sampling.X || texture.AddressY != sampling.Y ||
            texture.PlatformData.VTData?.IsInitialized() == true)
            throw new NotSupportedException("Color icon ingredient texture contract changed.");
        var decoded = texture.Decode() ?? throw new InvalidDataException("Color icon ingredient has no decodable mip.");
        if (decoded.Width != width || decoded.Height != height)
            throw new NotSupportedException("Color icon ingredient dimensions changed.");
        return new(decoded, srgb, sampling);
    }

    internal static IconParameters ReadParameters(UMaterialInstanceConstant material)
    {
        if (material.bHasStaticPermutationResource || material.StaticParameters is not null || material.TextureParameterValues.Length != 0)
            throw new NotSupportedException("Color icon material contains a static or texture override.");
        if (Properties.TryGet<FStructFallback>(material, "BasePropertyOverrides", out var overrides) &&
            overrides.Properties.Any(property => property.Name.Text.StartsWith("bOverride", StringComparison.Ordinal) && property.Tag?.GenericValue is not false))
            throw new NotSupportedException("Color icon material enables a base property override.");
        var scalars = new Dictionary<string, float>(StringComparer.Ordinal)
        {
            ["MetalA"] = 0,
            ["MetalB"] = 0,
            ["MetalC"] = 0,
            ["CoverageB"] = 0,
            ["CoverageC"] = 0,
            ["RefractionDepthBias"] = 0
        };
        var vectors = new Dictionary<string, Vector4>(StringComparer.Ordinal)
        {
            ["ColorA"] = new(1, 0, 1, 1),
            ["ColorB"] = Vector4.One,
            ["ColorC"] = new(.015625f, .015625f, .015625f, 1),
            ["SelectionColor"] = Vector4.Zero
        };
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var scalar in material.ScalarParameterValues)
        {
            VerifyParameter(scalar.Name, scalar.ParameterInfo, seen, scalars.ContainsKey(scalar.Name));
            if (!float.IsFinite(scalar.ParameterValue)) throw new NotSupportedException("Nonfinite color icon parameter.");
            scalars[scalar.Name] = scalar.ParameterValue;
        }
        foreach (var vector in material.VectorParameterValues)
        {
            VerifyParameter(vector.Name, vector.ParameterInfo, seen, vectors.ContainsKey(vector.Name));
            if (vector.ParameterValue is not { } value || !float.IsFinite(value.R) || !float.IsFinite(value.G) ||
                !float.IsFinite(value.B) || !float.IsFinite(value.A)) throw new NotSupportedException("Nonfinite color icon parameter.");
            vectors[vector.Name] = new(value.R, value.G, value.B, value.A);
        }
        static Vector3 Rgb(Vector4 value) => new(value.X, value.Y, value.Z);
        return new(Rgb(vectors["ColorA"]), Rgb(vectors["ColorB"]), Rgb(vectors["ColorC"]),
            scalars["MetalA"], scalars["MetalB"], scalars["MetalC"], scalars["CoverageB"], scalars["CoverageC"], vectors["SelectionColor"]);
    }

    private static void VerifyParameter(string name, FMaterialParameterInfo? info, HashSet<string> seen, bool known)
    {
        if (!known || !seen.Add(name) || info is not { Association: EMaterialParameterAssociation.GlobalParameter, Index: -1 })
            throw new NotSupportedException($"Unverified color icon parameter binding: {name}.");
    }

    internal static CTexture RenderPixels(IconParameters parameters, MaterialTexture background, MaterialTexture bevel) =>
        MaterialPixels.Render(background.Width, background.Height, uv => Pixel(uv, parameters, background, bevel));

    internal static Vector4 Pixel(Vector2 uv, IconParameters p, MaterialTexture background, MaterialTexture bevel)
    {
        var sample = background.Sample(uv);
        var diagonal = (uv.X - .5f) * .7071069f + (uv.Y - .5f) * .7071066f + .5f;
        var thresholdB = (1.2f - -.2f) * p.CoverageB - .2f;
        var thresholdC = (1.2f - -.2f) * p.CoverageC - .2f;
        var (color, metal) = (p.ColorA, p.MetalA);
        if (diagonal + thresholdB >= 1) (color, metal) = (p.ColorB, p.MetalB);
        if (diagonal + thresholdC >= 1) (color, metal) = (p.ColorC, p.MetalC);
        var lighting = 2 * (sample.Y + metal * (sample.X - sample.Y));
        var b = 1.5f - diagonal - thresholdB;
        var c = 1.5f - diagonal - thresholdC;
        color *= lighting * (2 * bevel.Sample(new(b, b)).X) * (2 * bevel.Sample(new(c, c)).X);
        if (!float.IsFinite(color.X) || !float.IsFinite(color.Y) || !float.IsFinite(color.Z))
            throw new NotSupportedException("Color icon lighting produced nonfinite RGB.");
        color = Vector3.Max(Vector3.Zero, Vector3.Lerp(color, new(p.Selection.X, p.Selection.Y, p.Selection.Z), p.Selection.W));
        return new(color, Math.Clamp(sample.W, 0, 1));
    }
}

internal sealed class MaterialTexture
{
    public int Width { get; }
    public int Height { get; }
    private readonly Vector4[] pixels;
    private readonly MaterialSampling sampling;

    public MaterialTexture(CTexture texture, bool srgb, MaterialSampling sampling)
    {
        if (sampling.Filter is not (TextureFilter.TF_Nearest or TextureFilter.TF_Bilinear) ||
            !SupportedAddress(sampling.X) || !SupportedAddress(sampling.Y))
            throw new NotSupportedException("Material sampling must specify an explicit supported filter and address modes.");
        if (texture.PixelFormat is not (EPixelFormat.PF_B8G8R8A8 or EPixelFormat.PF_R8G8B8A8) ||
            texture.Width <= 0 || texture.Height <= 0 || texture.Data.Length != checked(texture.Width * texture.Height * 4))
            throw new NotSupportedException("Material sampling requires decoded RGBA8 or BGRA8 pixels.");
        (Width, Height, this.sampling) = (texture.Width, texture.Height, sampling);
        pixels = new Vector4[Width * Height];
        for (var index = 0; index < pixels.Length; index++)
        {
            var offset = index * 4;
            var red = texture.PixelFormat == EPixelFormat.PF_B8G8R8A8 ? offset + 2 : offset;
            var blue = texture.PixelFormat == EPixelFormat.PF_B8G8R8A8 ? offset : offset + 2;
            pixels[index] = new(Channel(texture.Data[red], srgb), Channel(texture.Data[offset + 1], srgb),
                Channel(texture.Data[blue], srgb), texture.Data[offset + 3] / 255f);
        }
    }

    public Vector4 Sample(Vector2 uv)
    {
        var x = uv.X * Width;
        var y = uv.Y * Height;
        if (!float.IsFinite(x) || !float.IsFinite(y) || (double)x < int.MinValue + 1d || (double)x > int.MaxValue - 1d ||
            (double)y < int.MinValue + 1d || (double)y > int.MaxValue - 1d)
            throw new NotSupportedException("Material sample coordinates exceed the finite pixel range.");
        if (sampling.Filter == TextureFilter.TF_Nearest) return At((int)MathF.Floor(x), (int)MathF.Floor(y));
        x -= .5f;
        y -= .5f;
        var left = (int)MathF.Floor(x);
        var top = (int)MathF.Floor(y);
        return Vector4.Lerp(Vector4.Lerp(At(left, top), At(left + 1, top), x - left),
            Vector4.Lerp(At(left, top + 1), At(left + 1, top + 1), x - left), y - top);
    }

    private Vector4 At(int x, int y) => pixels[Address(y, Height, sampling.Y) * Width + Address(x, Width, sampling.X)];
    private static bool SupportedAddress(TextureAddress mode) => mode is TextureAddress.TA_Clamp or TextureAddress.TA_Wrap or TextureAddress.TA_Mirror;
    private static int Address(int value, int size, TextureAddress mode)
    {
        if (mode == TextureAddress.TA_Clamp) return Math.Clamp(value, 0, size - 1);
        var period = mode == TextureAddress.TA_Mirror ? size * 2 : size;
        var wrapped = ((value % period) + period) % period;
        return wrapped < size ? wrapped : period - wrapped - 1;
    }
    private static float Channel(byte value, bool srgb)
    {
        var normalized = value / 255f;
        return !srgb ? normalized : normalized <= .04045f ? normalized / 12.92f : MathF.Pow((normalized + .055f) / 1.055f, 2.4f);
    }
}
