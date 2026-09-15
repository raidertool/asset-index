using System.Numerics;
using CUE4Parse.UE4.Assets.Exports.Texture;
using CUE4Parse_Conversion.Textures;

namespace AssetIndex;

// Linear material RGBA to an sRGB PNG source, preserving straight alpha.
internal static class MaterialPixels
{
    public static CTexture Render(int width, int height, Func<Vector2, Vector4> pixel)
    {
        var pixels = new byte[checked(width * height * 4)];
        for (var y = 0; y < height; y++)
            for (var x = 0; x < width; x++)
            {
                var color = pixel(new((x + .5f) / width, (y + .5f) / height));
                var offset = (y * width + x) * 4;
                pixels[offset] = Byte(Srgb(color.X));
                pixels[offset + 1] = Byte(Srgb(color.Y));
                pixels[offset + 2] = Byte(Srgb(color.Z));
                pixels[offset + 3] = Byte(color.W);
            }
        return new(width, height, EPixelFormat.PF_R8G8B8A8, pixels);
    }

    private static float Srgb(float value) => value < .00313067f ? 12.92f * value : 1.055f * MathF.Pow(value, .4166667f) - .055f;
    private static byte Byte(float value)
    {
        if (!float.IsFinite(value)) throw new NotSupportedException("Material output contains a nonfinite channel.");
        return (byte)MathF.Round(Math.Clamp(value, 0, 1) * 255);
    }
}
