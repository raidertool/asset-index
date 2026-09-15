using System.Numerics;
using System.Security.Cryptography;
using CUE4Parse.GameTypes.Theia.FileProvider;
using CUE4Parse.UE4.Assets.Exports.Material;
using CUE4Parse.UE4.Assets.Exports.Texture;
using CUE4Parse.UE4.Objects.Engine;
using CUE4Parse.UE4.Objects.UObject;
using CUE4Parse_Conversion.Textures;

namespace AssetIndex;

// M_UI_Icon_Extractor: the Close Scrutiny icon's verified SM5 material.
internal sealed class ExtractorIcon(TheiaFileProvider provider)
{
    internal const string ParentPath = "/Game/Pioneer/UI/Assets/Icons/Map_Conditions/M_UI_Icon_Extractor.M_UI_Icon_Extractor";
    internal const string ParentHash = "dfd3b0429cac0fb1d3792bb080a18a858197a6ce0854c5aa86050c3ecadad073";
    internal const string TexturePath = "/Game/Pioneer/UI/Assets/Enemies/T_UI_Ping_Enemy_Extractor.T_UI_Ping_Enemy_Extractor";
    private MaterialTexture? texture;

    public CTexture Render(UMaterial material)
    {
        VerifyMaterial(material);
        if (texture is null)
        {
            var path = GameFiles.ResolvePackagePath(provider, ParentPath[..ParentPath.LastIndexOf('.')]);
            VerifyParent(provider[path].Read());
            if (material.CachedExpressionData is null ||
                !material.CachedExpressionData.TryGetValue<UTexture2D[]>(out var inputs, "ReferencedTextures") || inputs.Length != 1)
                throw new NotSupportedException("Extractor icon texture binding changed.");
            texture = ReadTexture(inputs[0]);
        }
        return RenderPixels(texture);
    }

    internal static void VerifyMaterial(UMaterial material)
    {
        if (material.GetPathName() != ParentPath ||
            !Properties.TryGet<FName>(material, "MaterialDomain", out var domain) || domain.Text != "EMaterialDomain::MD_UI" ||
            !Properties.TryGet<FName>(material, "BlendMode", out var blend) || blend.Text != "EBlendMode::BLEND_TranslucentGreyTransmittance")
            throw new NotSupportedException("Material does not match the verified extractor icon domain and blend mode.");
    }

    internal static void VerifyParent(byte[] package)
    {
        // Pins both compiled maps, numeric defaults and the texture-index binding.
        // Only the original UMaterial is accepted; material-instance overrides are unsupported.
        if (Convert.ToHexStringLower(SHA256.HashData(package)) != ParentHash)
            throw new NotSupportedException("Extractor icon package differs from the verified shader graph.");
    }

    private static MaterialTexture ReadTexture(UTexture2D? source)
    {
        if (source is not
            {
                SRGB: true, IsNormalMap: false,
                CompressionSettings: TextureCompressionSettings.TC_Default, Format: EPixelFormat.PF_DXT5,
                PlatformData:
                {
                    FirstMipToSerialize: 0, SizeX: 256, SizeY: 256, PackedData: 1, PixelFormat: "PF_DXT5",
                    Mips: [{ SizeX: 256, SizeY: 256, SizeZ: 1 }, ..]
                }
            } || source.GetPathName() != TexturePath || source.PlatformData.VTData?.IsInitialized() == true)
            throw new NotSupportedException("Extractor icon texture contract changed.");
        var decoded = source.DecodeMip(0) ?? throw new InvalidDataException("Extractor icon mip 0 could not be decoded.");
        if (decoded.Width != 256 || decoded.Height != 256)
            throw new NotSupportedException("Extractor icon mip dimensions changed.");
        return new(decoded, true, new(source.AddressX, source.AddressY, TextureFilter.TF_Bilinear));
    }

    // The standalone frame fixes the captured View-buffer input to zero. Its
    // reflection was stripped, so this is not labelled GameTime or RealTime.
    internal static CTexture RenderPixels(MaterialTexture source) =>
        MaterialPixels.Render(source.Width, source.Height, uv => Pixel(uv, 0, Vector4.Zero, source));

    internal static Vector4 Pixel(Vector2 uv, float viewInput, Vector4 selection, MaterialTexture source)
    {
        if (!float.IsFinite(viewInput) || !float.IsFinite(selection.X) || !float.IsFinite(selection.Y) ||
            !float.IsFinite(selection.Z) || !float.IsFinite(selection.W))
            throw new NotSupportedException("Extractor icon has a nonfinite uniform.");
        var alpha = source.Sample(uv).W;
        if (MathF.Abs(uv.X - .5f) <= .15f && MathF.Abs(uv.Y - .75f) <= .15f)
            alpha = MathF.Max(Pulse(uv.Y, viewInput), MathF.Max(Pulse(uv.Y, viewInput + 1.332f), Pulse(uv.Y, viewInput + 2.664f)));
        var color = Vector3.Max(Vector3.Zero, Vector3.One + selection.W * (new Vector3(selection.X, selection.Y, selection.Z) - Vector3.One));
        return new(color, Math.Clamp(alpha, 0, 1));
    }

    private static float Pulse(float y, float viewInput)
    {
        var phase = viewInput * .25f;
        phase -= MathF.Floor(phase);
        // Exact float32 immediates from the captured shader, including its origin.
        var position = (y - .59999996f) * 3.3333333f + phase;
        var edge = MathF.Max(1 - 1.4285715f * phase, .5f);
        var band = (edge >= position ? 0 : 1) - MathF.Floor(position);
        return band * Math.Clamp(1 - 10 * (phase - .3f), 0, 1);
    }
}
