using System.Numerics;
using System.Text.Json;
using CUE4Parse.UE4.Assets.Exports.Material;
using CUE4Parse.UE4.Assets.Exports.Texture;
using CUE4Parse.UE4.Assets.Objects;
using CUE4Parse.UE4.Assets.Objects.Properties;
using CUE4Parse.UE4.Objects.Engine;
using CUE4Parse.UE4.Objects.UObject;
using CUE4Parse_Conversion.Textures;

namespace AssetIndex.Tests;

public sealed class ExtractorIconTests
{
    private static readonly MaterialSampling Sampling = new(TextureAddress.TA_Clamp, TextureAddress.TA_Clamp, TextureFilter.TF_Bilinear);

    [Fact]
    public void MatchesIndependentCompiledShaderFixtures()
    {
        using var fixture = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "extractor-icon.json")));
        var cases = fixture.RootElement.GetProperty("cases").Deserialize<ArithmeticCase[]>(new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;
        Assert.Equal(48, cases.Length);
        foreach (var test in cases)
        {
            var color = ExtractorIcon.Pixel(new(test.Uv[0], test.Uv[1]), test.Time,
                new(test.Selection[0], test.Selection[1], test.Selection[2], test.Selection[3]), Solid(test.Alpha));
            float[] actual = [color.X, color.Y, color.Z, color.W];
            for (var channel = 0; channel < 4; channel++)
                Assert.InRange(Math.Abs(actual[channel] - test.Expected[channel]), 0, .000005);
        }
    }

    [Fact]
    public void TextureAlphaUsesBothUvAxesAndIsNotGammaConverted()
    {
        var source = new MaterialTexture(new CTexture(2, 2, EPixelFormat.PF_R8G8B8A8,
            [1, 2, 3, 32, 4, 5, 6, 64, 7, 8, 9, 128, 10, 11, 12, 255]), true, Sampling);

        Assert.Equal(new Vector4(1, 1, 1, 32 / 255f), ExtractorIcon.Pixel(new(.25f, .25f), 0, default, source));
        Assert.Equal(64 / 255f, ExtractorIcon.Pixel(new(.75f, .25f), 0, default, source).W);
        Assert.Equal(128 / 255f, ExtractorIcon.Pixel(new(.25f, .75f), 0, default, source).W);
        Assert.Equal(1, ExtractorIcon.Pixel(new(.75f, .75f), 0, default, source).W);
    }

    [Fact]
    public void PulseReplacesTextureAlphaOnlyInsideItsRectangle()
    {
        var source = Solid(255);
        Assert.Equal(0, ExtractorIcon.Pixel(new(.5f, .61f), 0, default, source).W);
        Assert.InRange(ExtractorIcon.Pixel(new(.5f, .7f), 0, default, source).W, .66999f, .67001f);
        Assert.Equal(1, ExtractorIcon.Pixel(new(.34f, .7f), 0, default, source).W);
        Assert.Equal(1, ExtractorIcon.Pixel(new(.66f, .7f), 0, default, source).W);
        Assert.Equal(1, ExtractorIcon.Pixel(new(.5f, .59f), 0, default, source).W);
        Assert.Equal(1, ExtractorIcon.Pixel(new(.5f, .91f), 0, default, source).W);
        Assert.NotEqual(ExtractorIcon.Pixel(new(.5f, .7f), 0, default, source).W,
            ExtractorIcon.Pixel(new(.5f, .7f), 1, default, source).W);
    }

    [Fact]
    public void SelectionRgbAndStraightAlphaUseTheSharedOutputEncoding()
    {
        var pixel = ExtractorIcon.Pixel(new(.1f), 0, new(.25f, .125f, .5f, 1), Solid(128));
        var output = MaterialPixels.Render(1, 1, _ => pixel);
        Assert.Equal(new byte[] { 137, 99, 188, 128 }, output.Data);
        Assert.Equal(new byte[] { 255, 255, 255, 128 }, ExtractorIcon.RenderPixels(Solid(128)).Data);
    }

    [Fact]
    public void RejectsChangedPackagesPathsDomainsAndBlendModes()
    {
        Assert.Throws<NotSupportedException>(() => ExtractorIcon.VerifyParent([1, 2, 3]));
        var material = Material("EMaterialDomain::MD_UI", "EBlendMode::BLEND_TranslucentGreyTransmittance");
        ExtractorIcon.VerifyMaterial(material);
        material.Name = "Changed";
        Assert.Throws<NotSupportedException>(() => ExtractorIcon.VerifyMaterial(material));
        Assert.Throws<NotSupportedException>(() => ExtractorIcon.VerifyMaterial(Material("EMaterialDomain::MD_Surface", "EBlendMode::BLEND_TranslucentGreyTransmittance")));
        Assert.Throws<NotSupportedException>(() => ExtractorIcon.VerifyMaterial(Material("EMaterialDomain::MD_UI", "EBlendMode::BLEND_Opaque")));
    }

    [Fact]
    public void RejectsNonfiniteUniformsAndCoordinates()
    {
        var source = Solid(128);
        foreach (var value in new[] { float.NaN, float.PositiveInfinity, float.NegativeInfinity })
        {
            Assert.Throws<NotSupportedException>(() => ExtractorIcon.Pixel(new(.5f), value, default, source));
            Assert.Throws<NotSupportedException>(() => ExtractorIcon.Pixel(new(.5f), 0, new(value), source));
            Assert.Throws<NotSupportedException>(() => ExtractorIcon.Pixel(new(value), 0, default, source));
        }
    }

    private static MaterialTexture Solid(byte alpha) => new(new CTexture(1, 1, EPixelFormat.PF_R8G8B8A8,
        [19, 37, 61, alpha]), true, Sampling);

    private static UMaterial Material(string domain, string blend)
    {
        var material = new UMaterial { Name = ExtractorIcon.ParentPath };
        material.Properties.Add(new FPropertyTag { Name = "MaterialDomain", Tag = new NameProperty(new FName(domain)) });
        material.Properties.Add(new FPropertyTag { Name = "BlendMode", Tag = new NameProperty(new FName(blend)) });
        return material;
    }

    private sealed record ArithmeticCase(float[] Uv, float Time, float[] Selection, byte Alpha, double[] Expected);
}
