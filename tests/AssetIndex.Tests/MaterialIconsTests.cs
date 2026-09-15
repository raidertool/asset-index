using System.Numerics;
using CUE4Parse.UE4.Assets.Exports.Material;
using CUE4Parse.UE4.Assets.Exports.Texture;
using CUE4Parse.UE4.Assets.Objects;
using CUE4Parse.UE4.Assets.Objects.Properties;
using CUE4Parse.UE4.Objects.Engine;
using SkiaSharp;
using CUE4Parse_Conversion.Textures;

namespace AssetIndex.Tests;

public sealed class MaterialIconsTests
{
    private static readonly MaterialSampling Nearest = new(TextureAddress.TA_Clamp, TextureAddress.TA_Clamp, TextureFilter.TF_Nearest);

    [Fact]
    public void BandsSelectAThenBThenCAndPreserveBackgroundAlpha()
    {
        var parameters = new IconParameters(Vector3.UnitX, Vector3.UnitY, Vector3.UnitZ, CoverageB: .75f, CoverageC: .25f);
        var background = Solid(255, 255, 0, 64);
        var bevel = Solid(255, 0, 0, 255);
        Assert.Equal(new Vector4(8, 0, 0, 64 / 255f), MaterialIcons.Pixel(new(.1f), parameters, background, bevel));
        Assert.Equal(new Vector4(0, 8, 0, 64 / 255f), MaterialIcons.Pixel(new(.5f), parameters, background, bevel));
        Assert.Equal(new Vector4(0, 0, 8, 64 / 255f), MaterialIcons.Pixel(new(.9f), parameters, background, bevel));
    }

    [Fact]
    public void MetalBlendsLightingChannelsBeforeApplyingColor()
    {
        var parameters = new IconParameters(new(.5f), Vector3.Zero, Vector3.Zero);
        var background = Solid(255, 0, 0, 255);
        var bevel = Solid(255, 0, 0, 255);
        Assert.Equal(new Vector4(0, 0, 0, 1), MaterialIcons.Pixel(new(.2f), parameters, background, bevel));
        Assert.Equal(new Vector4(4, 4, 4, 1), MaterialIcons.Pixel(new(.2f), parameters with { MetalA = 1 }, background, bevel));
        Assert.Equal(new Vector4(1, 1, 1, 1), MaterialIcons.Pixel(new(.2f), parameters with { MetalA = .25f }, background, bevel));
    }

    [Fact]
    public void SelectionAndSrgbEncodingPreserveStraightAlpha()
    {
        var parameters = new IconParameters(Vector3.One, Vector3.One, Vector3.One, Selection: new(.25f, .125f, .5f, 1));
        var texture = MaterialIcons.RenderPixels(parameters, Solid(255, 255, 255, 128), Solid(255, 0, 0, 255));
        Assert.Equal(new byte[] { 137, 99, 188, 128 }, texture.Data);
        using var codec = SKCodec.Create(new SKMemoryStream(Images.Encode(texture)));
        using var image = new SKBitmap(new SKImageInfo(1, 1, SKColorType.Rgba8888, SKAlphaType.Unpremul));
        Assert.Equal(SKCodecResult.Success, codec.GetPixels(image.Info, image.GetPixels()));
        Assert.Equal(new SKColor(137, 99, 188, 128), image.GetPixel(0, 0));
    }

    [Fact]
    public void BilinearFilteringDecodesSrgbBeforeInterpolating()
    {
        var texture = new MaterialTexture(new CTexture(2, 1, EPixelFormat.PF_R8G8B8A8,
            [0, 0, 0, 0, 255, 255, 255, 255]), true, Nearest with { Filter = TextureFilter.TF_Bilinear });
        Assert.Equal(new Vector4(.5f), texture.Sample(new(.5f)));
    }

    [Theory]
    [InlineData(TextureAddress.TA_Clamp, 0, 1)]
    [InlineData(TextureAddress.TA_Wrap, 1, 0)]
    [InlineData(TextureAddress.TA_Mirror, 0, 1)]
    public void AddressModesHandleNegativeAndOverflowCoordinates(TextureAddress mode, float negative, float positive)
    {
        var texture = new MaterialTexture(new CTexture(2, 1, EPixelFormat.PF_R8G8B8A8,
            [0, 0, 0, 255, 255, 0, 0, 255]), false, Nearest with { X = mode });
        Assert.Equal(negative, texture.Sample(new(-.25f, .5f)).X);
        Assert.Equal(positive, texture.Sample(new(1.25f, .5f)).X);
    }

    [Fact]
    public void BevelSamplesUseBothCoordinates()
    {
        var texture = new MaterialTexture(new CTexture(2, 2, EPixelFormat.PF_B8G8R8A8,
            [0, 0, 0, 255, 0, 0, 64, 255, 0, 0, 128, 255, 0, 0, 255, 255]), false, Nearest);
        Assert.Equal(128 / 255f, texture.Sample(new(.25f, .75f)).X);
        Assert.Equal(64 / 255f, texture.Sample(new(.75f, .25f)).X);
        var parameters = new IconParameters(Vector3.One, Vector3.One, Vector3.One, CoverageB: .9f, CoverageC: .7f);
        // At the center, the two boundary coordinates are -.06 and .22; both clamp to the black texel.
        Assert.Equal(new Vector4(0, 0, 0, 1), MaterialIcons.Pixel(new(.5f), parameters, Solid(255, 255, 255, 255), texture));
    }

    [Fact]
    public void RejectsImplicitFilteringAndUnknownParentPackage()
    {
        Assert.Throws<NotSupportedException>(() => new MaterialTexture(new CTexture(1, 1, EPixelFormat.PF_R8G8B8A8,
            [255, 255, 255, 255]), true, Nearest with { Filter = TextureFilter.TF_Default }));
        Assert.Throws<NotSupportedException>(() => MaterialIcons.VerifyParent([1, 2, 3]));
    }

    [Fact]
    public void RejectsTrilinearAndNonfiniteOrOverflowCoordinates()
    {
        Assert.Throws<NotSupportedException>(() => new MaterialTexture(new CTexture(1, 1, EPixelFormat.PF_R8G8B8A8,
            [255, 255, 255, 255]), false, Nearest with { Filter = TextureFilter.TF_Trilinear }));
        var texture = Solid(255, 255, 255, 255);
        foreach (var coordinate in new[] { float.NaN, float.PositiveInfinity, float.NegativeInfinity, float.MaxValue, (float)int.MaxValue })
            Assert.Throws<NotSupportedException>(() => texture.Sample(new(coordinate, .5f)));
    }

    [Fact]
    public void RejectsNonfiniteRgbBeforeEncoding()
    {
        var parameters = new IconParameters(new(float.MaxValue), Vector3.Zero, Vector3.Zero);
        Assert.Throws<NotSupportedException>(() => MaterialIcons.RenderPixels(parameters, Solid(255, 255, 255, 255), Solid(255, 0, 0, 255)));
        parameters = parameters with { ColorA = Vector3.One, Selection = new(float.NaN) };
        Assert.Throws<NotSupportedException>(() => MaterialIcons.RenderPixels(parameters, Solid(255, 255, 255, 255), Solid(255, 0, 0, 255)));
    }

    [Fact]
    public void ReadsVerifiedDefaultsAndOneExplicitOverride()
    {
        var material = new UMaterialInstanceConstant { ScalarParameterValues = [Scalar("MetalA", .75f)] };
        var parameters = MaterialIcons.ReadParameters(material);
        Assert.Equal(new Vector3(1, 0, 1), parameters.ColorA);
        Assert.Equal(new Vector3(.015625f), parameters.ColorC);
        Assert.Equal(Vector4.Zero, parameters.Selection);
        Assert.Equal(.75f, parameters.MetalA);
    }

    [Fact]
    public void RejectsUnknownDuplicateNonfiniteAndLayerParameters()
    {
        foreach (var scalars in new[]
        {
            new[] { Scalar("Unknown", 0) },
            new[] { Scalar("MetalA", 0), Scalar("MetalA", 1) },
            new[] { Scalar("MetalA", float.NaN) },
            new[] { Scalar("MetalA", 0, EMaterialParameterAssociation.LayerParameter) }
        }) Assert.Throws<NotSupportedException>(() => MaterialIcons.ReadParameters(new UMaterialInstanceConstant { ScalarParameterValues = scalars }));
    }

    [Theory]
    [InlineData("bOverride_BlendMode", false)]
    [InlineData("bOverride_BlendMode", true)]
    [InlineData("boverride_blendmode", false)]
    [InlineData("boverride_blendmode", true)]
    [InlineData("BOVERRIDE_BLENDMODE", false)]
    [InlineData("BOVERRIDE_BLENDMODE", true)]
    public void OnlyEnabledBaseOverridesChangeTheGraph(string flag, bool enabled)
    {
        var material = new UMaterialInstanceConstant();
        material.Properties.Add(Tag("BasePropertyOverrides", new FStructFallback([Tag(flag, enabled)])));
        if (enabled) Assert.Throws<NotSupportedException>(() => MaterialIcons.ReadParameters(material));
        else Assert.NotNull(MaterialIcons.ReadParameters(material));
    }

    private static MaterialTexture Solid(byte r, byte g, byte b, byte a) => new(new CTexture(1, 1, EPixelFormat.PF_R8G8B8A8,
        [r, g, b, a]), false, Nearest);

    private static FScalarParameterValue Scalar(string name, float value, EMaterialParameterAssociation association = EMaterialParameterAssociation.GlobalParameter)
        => new(new FStructFallback([Tag("ParameterInfo", new FMaterialParameterInfo { Name = name, Index = -1, Association = association }), Tag("ParameterValue", value)]));

    private static FPropertyTag Tag<T>(string name, T value) => new() { Name = name, Tag = new TestValue<T>(value) };
    private sealed class TestValue<T>(T value) : FPropertyTagType<T>
    {
        public override object? GenericValue => value;
    }
}
