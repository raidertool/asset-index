using System.Runtime.CompilerServices;
using CUE4Parse.FileProvider.Objects;
using CUE4Parse.MappingsProvider;
using CUE4Parse.MappingsProvider.Usmap;
using CUE4Parse.UE4.Assets;
using CUE4Parse.UE4.Assets.Exports;
using CUE4Parse.UE4.Assets.Objects;
using CUE4Parse.UE4.Assets.Objects.Properties;
using CUE4Parse.UE4.Objects.UObject;
using CUE4Parse_Conversion.Textures;
using SkiaSharp;

namespace AssetIndex.Tests;

public sealed partial class ImagePlanTests : IDisposable
{
    private static readonly Lazy<TypeMappings> Mappings = new(() =>
        new FileUsmapTypeMappingsProvider(Path.Combine(AppContext.BaseDirectory, "mappings", "ArcRaiders.usmap")).MappingsForGame!);
    private readonly string output = Path.Combine(Path.GetTempPath(), "asset-index-image-plan-" + Guid.NewGuid().ToString("N"));
    private readonly List<ExtractionIssue> issues = [];

    [Fact]
    public void DetachedPlanReleasesObjectsAndReloadsTheExactMountedFile()
    {
        TextureDecoder.UseAssetRipperTextureDecoder = true;
        var original = new ImageFixtureFile("/Game/Icons.uasset");
        var update = new ImageFixtureFile(original.Path, 0x001f);
        using var provider = new ImageProvider();
        var (source, weakPackage, weakSource, weakTarget, weakCallback) = Capture(provider, original, issues);
        provider.Files.AddFiles(new Dictionary<string, GameFile> { [update.Path] = update });
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        Assert.False(weakPackage.IsAlive);
        Assert.False(weakSource.IsAlive);
        Assert.False(weakTarget.IsAlive);
        Assert.False(weakCallback.IsAlive);

        var resources = new ImageResources(output, issues);
        var result = Assert.Single(Images.Export(new(42, [source], []), resources, provider.Load, issues));

        Assert.Equal("exported", result.Status);
        Assert.Equal("images/Game/Icon.png", result.File);
        Assert.Equal(2, provider.ReadFiles.Count);
        Assert.All(provider.ReadFiles, file => Assert.Same(original, file));
        using var bitmap = SKBitmap.Decode(Path.Combine(output, result.File!));
        Assert.Equal(SKColors.Red, bitmap.GetPixel(0, 0));
        Assert.Empty(issues);
    }

    [Fact]
    public void CapturePreservesExplicitNullTemplateLookupFieldSpellingAndSource()
    {
        using var provider = new ImageProvider();
        var package = provider.LoadPackage(new ImageFixtureFile("/Game/Icons.uasset"));
        var template = new UObject([Reference("Icon", package), Reference("bigicon", package)]) { Name = "Template" };
        var source = new UObject([new FPropertyTag { Name = "icon", Tag = new ObjectProperty(new FPackageIndex()) }])
        { Name = "Source", Template = new ResolvedLoadedObject(template) };

        var requests = Images.Capture(source, Mappings.Value, provider.Locate, issues);

        Assert.Collection(requests,
            absent =>
            {
                Assert.Equal("icon", absent.Field);
                Assert.Equal("absent", absent.Status);
                Assert.Null(absent.Resource);
                Assert.Null(absent.Location);
            },
            inherited =>
            {
                Assert.Equal("bigicon", inherited.Field);
                Assert.Equal("pending", inherited.Status);
                Assert.Equal("/Game/Icons.Icon", inherited.Resource);
                Assert.NotNull(inherited.Location);
            });
        Assert.All(requests, request => Assert.Equal(source.GetPathName(), request.Source));
        Assert.Empty(issues);
    }

    [Fact]
    public void CaptureFailurePreservesTheResolvedTargetPath()
    {
        using var provider = new ImageProvider();
        var package = provider.LoadPackage(new ImageFixtureFile("/Game/Icons.uasset"));
        var source = new UObject([Reference("Icon", package)]) { Name = "Source" };
        var request = Assert.Single(Images.Capture(source, Mappings.Value, _ => throw new InvalidDataException("No export index."), issues));
        Assert.Equal("failed", request.Status);
        Assert.Equal("/Game/Icons.Icon", request.Resource);
        Assert.Null(request.Location);
        Assert.Equal("Source.Icon", Assert.Single(issues).Path);
    }

    [Theory]
    [InlineData("UIClanLogoMetaDataItem", "texture", false)]
    [InlineData("UIClanLogoMetaDataItem", "Texture", true)]
    [InlineData("UIClanLogoMetaDataItem", "TypeImage", false)]
    [InlineData("UIClanBackgroundMetaDataItem", "TypeImage", false)]
    [InlineData("UIClanBorderMetaDataItem", "TypeImage", false)]
    [InlineData("UIEnvironmentalDamageSourceMetaDataItem", "CoverImage", false)]
    [InlineData("UIEnvironmentalDamageSourceMetaDataItem", "coverIMAGE", true)]
    public void ClassSpecificImagesUseNativeDeclarationsIncludingRuntimeSubclassesAndTemplates(
        string type, string field, bool inherited)
    {
        using var provider = new ImageProvider();
        var file = new ImageFixtureFile("Icons.uasset");
        provider.Files.AddFiles(new Dictionary<string, GameFile> { [file.Path] = file });
        var package = provider.LoadPackage(file);
        var source = TypedSource(type);
        var owner = inherited ? TypedSource(type) : source;
        owner.Properties.Add(new() { Name = field, Tag = new SoftObjectProperty(new FSoftObjectPath("Icons.Icon", "", package)) });
        if (inherited)
        {
            owner.Name = "Template";
            source.Template = new ResolvedLoadedObject(owner);
            RuntimeClassFixture.Derive(source);
        }

        var request = Assert.Single(Images.Capture(source, Mappings.Value, provider.Locate, issues));

        Assert.Equal("pending", request.Status);
        Assert.Equal(field, request.Field);
        Assert.Equal("Source", request.Source);
        Assert.Equal("Icons.Icon", request.Resource);
        Assert.Same(file, request.Location!.File);
        Assert.Empty(issues);
    }

    [Fact]
    public void ClassSpecificImageNullOverridesTemplateWithoutLoadingIt()
    {
        var source = TypedSource("UIClanLogoMetaDataItem");
        source.Properties.Add(new() { Name = "Texture", Tag = new SoftObjectProperty(default) });
        var template = TypedSource("UIClanLogoMetaDataItem");
        template.Properties.Add(new() { Name = "Texture", Tag = new SoftObjectProperty(new FSoftObjectPath("NotLoaded.Image", "")) });
        source.Template = new ResolvedLoadedObject(template);

        var request = Assert.Single(Images.Capture(source, Mappings.Value,
            _ => throw new InvalidOperationException("Null image must not load."), issues));

        Assert.Equal("absent", request.Status);
        Assert.Null(request.Resource);
        Assert.Null(request.Location);
        Assert.Empty(issues);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ClassSpecificFieldNamesCannotInventImageRolesForUnrelatedClasses(bool runtimeImpostor)
    {
        var source = TypedSource("Object");
        source.Properties.Add(new() { Name = "Texture", Tag = new SoftObjectProperty(default) });
        source.Properties.Add(new() { Name = "TypeImage", Tag = new SoftObjectProperty(default) });
        source.Properties.Add(new() { Name = "CoverImage", Tag = new SoftObjectProperty(default) });
        if (runtimeImpostor) RuntimeClassFixture.Derive(source, "UIClanLogoMetaDataItem");

        Assert.Empty(Images.Capture(source, Mappings.Value,
            _ => throw new InvalidOperationException("Unrelated reference must not load."), issues));
        Assert.Empty(issues);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ClassSpecificImageSchemaOrValueFailuresRemainExplicit(bool missingParent)
    {
        var source = TypedSource("UIClanLogoMetaDataItem");
        source.Properties.Add(new() { Name = "Texture", Tag = new Int64Property(42) });
        if (missingParent) RuntimeClassFixture.Derive(source).Super = null;

        var request = Assert.Single(Images.Capture(source, Mappings.Value,
            _ => throw new InvalidOperationException("Invalid image must not load."), issues));

        Assert.Equal("failed", request.Status);
        Assert.Null(request.Resource);
        Assert.Equal("Source.Texture", Assert.Single(issues).Path);
    }

    [Fact]
    public void ExportRendersOnceAcrossDefinitionsAndMetadataAndKeepsTheirOrder()
    {
        TextureDecoder.UseAssetRipperTextureDecoder = true;
        using var provider = new ImageProvider();
        var file = new ImageFixtureFile("/Game/Icons.uasset");
        var location = new ObjectLocation(file, 0, "/Game/Icons.Icon");
        var definition = Source("Definition", [new("Icon", "Definition", location.Path, "pending", location)]);
        var metadataLocation = location with { Path = location.Path.ToLowerInvariant() };
        var metadata = Source("Metadata", [new("BigIcon", "Metadata", metadataLocation.Path, "pending", metadataLocation)]);
        var resources = new ImageResources(output, issues);
        var loads = 0;
        var result = Images.Export(new(42, [definition], [metadata]), resources, requested =>
        {
            loads++;
            return provider.Load(requested);
        }, issues);
        Assert.Equal(1, loads);
        Assert.Single(resources.Entries);
        Assert.Equal(["Icon", "BigIcon"], result.Select(image => image.Field));
        Assert.Equal(["Definition", "Metadata"], result.Select(image => image.Source));
        Assert.All(result, image =>
        {
            Assert.Equal("exported", image.Status);
            Assert.Equal(Assert.Single(resources.Entries).Path, image.Resource);
        });
        Assert.Equal(result[0].File, result[1].File);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    public void ConflictingPhysicalExportsFailWithoutReplacingTheFirstImage(bool sameFile, bool differentCase)
    {
        TextureDecoder.UseAssetRipperTextureDecoder = true;
        using var provider = new ImageProvider();
        var file = new ImageFixtureFile("/Game/Icons.uasset");
        var firstLocation = new ObjectLocation(file, 0, "/Game/Icons.Icon");
        var secondLocation = new ObjectLocation(sameFile ? file : new ImageFixtureFile(file.Path, 0x001f),
            sameFile ? 1 : 0, differentCase ? firstLocation.Path.ToLowerInvariant() : firstLocation.Path);
        var resources = new ImageResources(output, issues);
        var loads = 0;
        UObject Load(ObjectLocation location)
        {
            loads++;
            return provider.Load(location);
        }
        var first = resources.Export(firstLocation, Load);
        Assert.Equal("exported", first.Status);
        var filename = Path.Combine(output, first.File!);
        var bytes = File.ReadAllBytes(filename);
        var definition = Source("Definition", [new("Icon", "Definition", firstLocation.Path, "pending", firstLocation)]);
        var metadata = Source("Metadata", [new("BigIcon", "Metadata", secondLocation.Path, "pending", secondLocation)]);

        var images = Images.Export(new(42, [definition], [metadata]), resources, Load, issues);

        Assert.Equal(["exported", "failed"], images.Select(image => image.Status));
        Assert.Null(images[1].File);
        Assert.Same(first, Assert.Single(resources.Entries));
        Assert.Equal(bytes, File.ReadAllBytes(filename));
        Assert.Single(Directory.GetFiles(output, "*.png", SearchOption.AllDirectories));
        Assert.Equal(1, loads);
        Assert.Equal(secondLocation.Path, Assert.Single(issues).Path);
        Assert.Contains("conflicting physical exports", issues[0].Message);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void LoadFailuresAreFailedResourcesAndAreNotRetried(bool unsupported)
    {
        var location = new ObjectLocation(new ImageFixtureFile("/Game/Icons.uasset"), 0, "/Game/Icons.Icon");
        var resources = new ImageResources(output, issues);
        var loads = 0;
        UObject Load(ObjectLocation _)
        {
            loads++;
            throw unsupported ? new NotSupportedException("Cannot load package.") : new InvalidDataException("Cannot load package.");
        }
        var first = resources.Export(location, Load);
        var repeated = resources.Export(location, Load);
        Assert.Same(first, repeated);
        Assert.Equal("failed", first.Status);
        Assert.Equal(location.Path, first.Path);
        Assert.Equal(1, loads);
        Assert.Equal(location.Path, Assert.Single(issues).Path);
    }

    [Theory]
    [InlineData("/Game/Other.Icon")]
    [InlineData("/Game/Other.icon")]
    public void DifferentPackagesCannotOverwriteTheSameOriginalTextureFilename(string secondPath)
    {
        TextureDecoder.UseAssetRipperTextureDecoder = true;
        using var provider = new ImageProvider();
        var firstLocation = new ObjectLocation(new ImageFixtureFile("/Game/Icons.uasset"), 0, "/Game/Icons.Icon");
        var secondLocation = new ObjectLocation(new ImageFixtureFile("/Game/Other.uasset", 0x001f), 0, secondPath);
        var resources = new ImageResources(output, issues);
        var first = resources.Export(firstLocation, provider.Load);
        var bytes = File.ReadAllBytes(Path.Combine(output, first.File!));

        var second = resources.Export(secondLocation, provider.Load);

        Assert.Equal("failed", second.Status);
        Assert.Null(second.File);
        Assert.Equal(bytes, File.ReadAllBytes(Path.Combine(output, first.File!)));
        Assert.Contains("filename conflicts", Assert.Single(issues).Message);
        Assert.Single(Directory.GetFiles(output, "*.png", SearchOption.AllDirectories));
    }

    [Fact]
    public void AbsentAndCaptureFailurePlansDoNotLoadResources()
    {
        var source = Source("Source", [new("Icon", "Source", null, "absent"), new("BigIcon", "Source", "Bad", "failed")]);
        var resources = new ImageResources(output, issues);
        var result = Images.Export(new(42, [source], []), resources,
            _ => throw new InvalidOperationException("Must not load."), issues);
        Assert.Equal(["absent", "failed"], result.Select(image => image.Status));
        Assert.Empty(resources.Entries);
        Assert.Empty(issues);
    }

    [Fact]
    public void ReloadedWrongPathCannotBecomeAnImageResource()
    {
        var location = new ObjectLocation(new ImageFixtureFile("/Game/Icons.uasset"), 0, "/Game/Icons.Icon");
        var resources = new ImageResources(output, issues);
        var result = resources.Export(location, _ => new UObject { Name = "Wrong" });
        Assert.Equal("failed", result.Status);
        Assert.Equal(location.Path, Assert.Single(resources.Entries).Path);
        Assert.Contains("does not match", Assert.Single(issues).Message);
    }

    [Fact]
    public void NestedImageCaptureAndReloadValidationDoNotDecodeOuterBodies()
    {
        TextureDecoder.UseAssetRipperTextureDecoder = true;
        var package = new NestedImagePackage();
        var definition = package.ExportsLazy[2].Value;
        var location = new ObjectLocation(new ImageFixtureFile("/Game/Nested.uasset"), 1, "/Game/Nested.Parent:Icon");
        var request = Assert.Single(Images.Capture(definition, Mappings.Value, _ => location, issues));
        Assert.Equal("/Game/Nested.Parent:Source", request.Source);
        Assert.Equal(location.Path, request.Resource);
        Assert.Equal("pending", request.Status);

        var resources = new ImageResources(output, issues);
        var image = Assert.Single(Images.Export(new(42, [Source(request.Source, [request])], []),
            resources, point => package.ExportsLazy[point.ExportIndex].Value, issues));
        Assert.Equal("exported", image.Status);
        Assert.Equal(image.File, resources.Export(package.ExportsLazy[1].Value).File);
        Assert.False(package.ExportsLazy[0].IsValueCreated);
        Assert.Equal(0, package.OuterLoads);
        Assert.Empty(issues);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static (CatalogSource, WeakReference, WeakReference, WeakReference, WeakReference) Capture(
        ImageProvider provider, ImageFixtureFile file, List<ExtractionIssue> issues)
    {
        var package = provider.LoadPackage(file);
        var target = package.ExportsLazy[0].Value;
        var source = new UObject([Reference("Icon", package)]) { Name = "Definition" };
        var capture = new LocationCapture(provider, target);
        var requests = Images.Capture(source, Mappings.Value, capture.Locate, issues);
        return (Source(source.Name, requests), new(package), new(source), new(target), new(capture));
    }

    private sealed record LocationCapture(ImageProvider Provider, UObject Target)
    {
        public ObjectLocation Locate(UObject source)
        {
            Assert.Same(Target, source);
            return Provider.Locate(source);
        }
    }

    private static CatalogSource Source(string path, IReadOnlyList<ImageRequest> requests) => new(new(path, "DataAsset", path), [], requests);
    private static UObject TypedSource(string type) => new()
    {
        Name = "Source",
        Class = new ResolvedLoadedObject(new UScriptClass(type))
    };
    private static FPropertyTag Reference(string field, IPackage package) => new()
    {
        Name = field,
        Tag = new ObjectProperty(new FPackageIndex(package, 1))
    };

    private sealed class ImageProvider() : PackageProvider(Path.GetTempPath())
    {
        public List<GameFile> ReadFiles { get; } = [];
        protected override IPackage ReadPackage(GameFile file)
        {
            ReadFiles.Add(file);
            return new ImagePackage(file.Path[..^7], ((ImageFixtureFile)file).Color, this);
        }
    }

    private sealed class ImagePackage : AbstractUePackage
    {
        public ImagePackage(string path, ushort color, ImageProvider provider) : base(path, provider)
        {
            ExportsLazy = [new(() => new ImagesTests.CompressedTexture(color)
            {
                Name = "Icon", Outer = new ResolvedPackageObject(this)
            })];
        }
        public override FPackageFileSummary Summary => throw new NotSupportedException();
        public override FNameEntrySerialized[] NameMap => [];
        public override int ImportMapLength => 0;
        public override int ExportMapLength => 1;
        public override int GetExportIndex(string name, StringComparison comparisonType = StringComparison.Ordinal) => 0;
        public override ResolvedObject? ResolvePackageIndex(FPackageIndex? index) => index is { Index: 1 }
            ? new ResolvedLoadedObject(ExportsLazy[0].Value) : null;
    }

    private sealed class NestedImagePackage : AbstractUePackage
    {
        public int OuterLoads { get; private set; }
        public NestedImagePackage() : base("/Game/Nested", null)
        {
            ExportsLazy =
            [
                new(() => { OuterLoads++; throw new InvalidOperationException("Outer body must stay unloaded."); }),
                new(() => new ImagesTests.CompressedTexture { Name = "Icon", Outer = new Header(this, 0) }),
                new(() => new UObject([new FPropertyTag { Name = "Icon", Tag = new ObjectProperty(new FPackageIndex(this, 2)) }])
                    { Name = "Source", Outer = new Header(this, 0) })
            ];
        }
        public override FPackageFileSummary Summary => throw new NotSupportedException();
        public override FNameEntrySerialized[] NameMap => [];
        public override int ImportMapLength => 0;
        public override int ExportMapLength => 3;
        public override int GetExportIndex(string name, StringComparison comparisonType = StringComparison.Ordinal) => throw new NotSupportedException();
        public override ResolvedObject? ResolvePackageIndex(FPackageIndex? index) => index is { Index: > 0 and <= 3 }
            ? new Header(this, index.Index - 1) : null;

        private sealed class Header(NestedImagePackage package, int index) : ResolvedObject(package, index)
        {
            public override FName Name => new(ExportIndex == 0 ? "Parent" : ExportIndex == 1 ? "Icon" : "Source");
            public override ResolvedObject Outer => ExportIndex == 0 ? new ResolvedPackageObject(Package) : new Header((NestedImagePackage)Package, 0);
        }
    }

    public void Dispose()
    {
        if (Directory.Exists(output)) Directory.Delete(output, recursive: true);
    }
}
