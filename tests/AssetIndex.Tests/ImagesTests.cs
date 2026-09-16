using CUE4Parse.Compression;
using CUE4Parse.FileProvider.Objects;
using CUE4Parse.MappingsProvider;
using CUE4Parse.MappingsProvider.Usmap;
using CUE4Parse.UE4.Assets;
using CUE4Parse.UE4.Assets.Exports;
using CUE4Parse.UE4.Assets.Exports.Texture;
using CUE4Parse.UE4.Assets.Objects;
using CUE4Parse.UE4.Assets.Objects.Properties;
using CUE4Parse.UE4.Objects.UObject;
using CUE4Parse.UE4.Readers;
using CUE4Parse_Conversion.Textures;
using SkiaSharp;

namespace AssetIndex.Tests;

public sealed class ImagesTests
{
    private static readonly Lazy<TypeMappings> Mappings = new(() =>
        new FileUsmapTypeMappingsProvider(Path.Combine(AppContext.BaseDirectory, "mappings", "ArcRaiders.usmap")).MappingsForGame!);
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

    [Theory]
    [InlineData(false, "Icon", false)]
    [InlineData(false, "icon", false)]
    [InlineData(true, "Icon", false)]
    [InlineData(false, "ModifierIcon", true)]
    [InlineData(false, "ObscuredPreviewImage", true)]
    [InlineData(false, "OptionalLocationIcon", true)]
    [InlineData(false, "UnlockVideoPreviewImage", true)]
    [InlineData(true, "ModifierIcon", true)]
    public void ExportPreservesResolvedTexturePathEvenWhenDecodingFails(bool missingMip, string field, bool metadata)
    {
        TextureDecoder.UseAssetRipperTextureDecoder = true;
        var texture = new CompressedTexture { Name = "KnownTexture" };
        if (missingMip) texture.PlatformData.Mips = [];
        var package = new TexturePackage(texture);
        var definition = new UObject([new FPropertyTag
        {
            Name = field, Tag = new ObjectProperty(new FPackageIndex(package, 1))
        }])
        { Name = "KnownDefinition" };
        var output = Path.Combine(Path.GetTempPath(), "asset-index-image-test-" + Guid.NewGuid().ToString("N"));
        var issues = new List<ExtractionIssue>();
        try
        {
            var resources = new ImageResources(output, issues);
            var location = new ObjectLocation(new ImageFixtureFile("KnownTexture.uasset"), 0, texture.GetPathName());
            var source = new CatalogSource(new(definition.Name, definition.ExportType, definition.GetPathName()), [],
                Images.Capture(definition, Mappings.Value, _ => location, issues));
            var image = Assert.Single(Images.Export(new CatalogAsset(42, metadata ? [] : [source], metadata ? [source] : []),
                resources, _ => texture, issues));
            Assert.Equal(image.Resource, Assert.Single(resources.Entries).Path);
            Assert.Equal(texture.GetPathName(), image.Resource);
            Assert.Equal(field, image.Field);
            Assert.Equal(definition.GetPathName(), image.Source);
            if (missingMip)
            {
                Assert.Equal("failed", image.Status);
                Assert.Null(image.File);
                Assert.Equal("Texture has no decodable mip.", Assert.Single(issues).Message);
            }
            else
            {
                Assert.Equal("exported", image.Status);
                Assert.Empty(issues);
                using var bitmap = SKBitmap.Decode(Path.Combine(output, image.File!));
                Assert.Equal(SKColors.Red, bitmap.GetPixel(3, 3));
            }
        }
        finally
        {
            if (Directory.Exists(output)) Directory.Delete(output, recursive: true);
        }
    }

    private sealed class TexturePackage(UObject texture) : AbstractUePackage("Fixture", null)
    {
        public override FPackageFileSummary Summary => throw new NotSupportedException();
        public override FNameEntrySerialized[] NameMap => [];
        public override int ImportMapLength => 0;
        public override int ExportMapLength => 1;
        public override int GetExportIndex(string name, StringComparison comparisonType = StringComparison.Ordinal) => 0;
        public override ResolvedObject? ResolvePackageIndex(FPackageIndex? index) => index is { Index: 1 }
            ? new ResolvedLoadedObject(texture)
            : null;
    }

    internal sealed class CompressedTexture : UTexture2D
    {
        public CompressedTexture(ushort color = 0xf800)
        {
            Format = EPixelFormat.PF_DXT1;
            PlatformData.Mips = [new FTexture2DMipMap(new FByteArrayData([(byte)color, (byte)(color >> 8), 224, 7, 0, 0, 0, 0]), 4, 4, 1)];
        }
    }
}

internal sealed class ImageFixtureFile(string path, ushort color = 0xf800) : GameFile(path, 0)
{
    public ushort Color { get; } = color;
    public override bool IsEncrypted => false;
    public override CompressionMethod CompressionMethod => CompressionMethod.None;
    public override byte[] Read(FByteBulkDataHeader? header = null) => throw new NotSupportedException();
    public override FArchive CreateReader(FByteBulkDataHeader? header = null) => throw new NotSupportedException();
}
