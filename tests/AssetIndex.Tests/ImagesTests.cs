using CUE4Parse.UE4.Assets.Exports.Texture;
using CUE4Parse.UE4.Assets.Objects;
using CUE4Parse_Conversion.Textures;
using SkiaSharp;

namespace AssetIndex.Tests;

public sealed class ImagesTests
{
    [Fact]
    public void DecodesCompressedTextureWithManagedDecoder()
    {
        TextureDecoder.UseAssetRipperTextureDecoder = true;
        var texture = new CompressedTexture();
        var decoded = texture.Decode();
        Assert.NotNull(decoded);
        using var bitmap = SKBitmap.Decode(Images.Encode(decoded));
        Assert.Equal(4, bitmap.Width);
        Assert.Equal(SKColors.Red, bitmap.GetPixel(3, 3));
    }

    [Fact]
    public void EncodesPixelsWithRealNativePngLibrary()
    {
        var texture = new CTexture(2, 1, EPixelFormat.PF_R8G8B8A8, [255, 0, 0, 255, 0, 255, 0, 255]);
        var png = Images.Encode(texture);
        using var decoded = SKBitmap.Decode(png);
        Assert.Equal(2, decoded.Width);
        Assert.Equal(1, decoded.Height);
        Assert.Equal(SKColors.Red, decoded.GetPixel(0, 0));
        Assert.Equal(SKColors.Lime, decoded.GetPixel(1, 0));
    }

    [Fact]
    public void RejectsEmptyImage()
    {
        var texture = new CTexture(0, 0, EPixelFormat.PF_R8G8B8A8, []);
        Assert.Throws<InvalidDataException>(() => Images.Encode(texture));
    }

    private sealed class CompressedTexture : UTexture2D
    {
        public CompressedTexture()
        {
            Format = EPixelFormat.PF_DXT1;
            PlatformData.Mips = [new FTexture2DMipMap(new FByteArrayData([0, 248, 224, 7, 0, 0, 0, 0]), 4, 4, 1)];
        }
    }
}
