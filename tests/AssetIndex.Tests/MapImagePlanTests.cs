using CUE4Parse.FileProvider.Objects;
using CUE4Parse.MappingsProvider;
using CUE4Parse.UE4.Assets;
using CUE4Parse.UE4.Assets.Objects;
using CUE4Parse.UE4.Assets.Objects.Properties;
using CUE4Parse.UE4.Objects.UObject;

namespace AssetIndex.Tests;

public sealed partial class ImagePlanTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void MapAreaImagesKeepOrdinalSpellingNullAndTemplateOwnership(bool inherited)
    {
        using var provider = new ImageProvider();
        var file = new ImageFixtureFile("MapIcons.uasset");
        provider.Files.AddFiles(new Dictionary<string, GameFile> { [file.Path] = file });
        var package = provider.LoadPackage(file);
        var source = TypedSource("UIMapAreaInfoMetaDataItem");
        var owner = inherited ? TypedSource("UIMapAreaInfoMetaDataItem") : source;
        owner.Properties.Add(Areas("mapAreas", Structure(), Structure(Soft("HEADERimage")),
            Structure(Soft("HeaderImage", package))));
        if (inherited)
        {
            source.Template = new ResolvedLoadedObject(owner);
            RuntimeClassFixture.Derive(source);
        }

        var requests = Images.Capture(source, Mappings.Value, provider.Locate, issues);

        Assert.Collection(requests,
            absent => { Assert.Equal("mapAreas[1].HEADERimage", absent.Field); Assert.Equal("absent", absent.Status); },
            image =>
            {
                Assert.Equal("mapAreas[2].HeaderImage", image.Field);
                Assert.Equal("pending", image.Status);
                Assert.Equal("MapIcons.Icon", image.Resource);
                Assert.Same(file, image.Location!.File);
            });
        Assert.All(requests, image => Assert.Equal("Source", image.Source));
        Assert.Empty(issues);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void MapTextureHasItsOwnFieldPathAndDoesNotRenderMapMaterial(bool inherited)
    {
        using var provider = new ImageProvider();
        var file = new ImageFixtureFile("MapIcons.uasset");
        provider.Files.AddFiles(new Dictionary<string, GameFile> { [file.Path] = file });
        var package = provider.LoadPackage(file);
        var source = TypedSource("UIMapWidgetMetaDataItem");
        var owner = inherited ? TypedSource("UIMapWidgetMetaDataItem") : source;
        owner.Properties.Add(Settings("mapWidgetSettings", Soft("mapTEXTURE", package),
            new() { Name = "MapMaterial", Tag = new SoftObjectProperty(new FSoftObjectPath("NeverLoaded.Material", "")) }));
        if (inherited) source.Template = new ResolvedLoadedObject(owner);

        var request = Assert.Single(Images.Capture(source, Mappings.Value, provider.Locate, issues));

        Assert.Equal("mapWidgetSettings.mapTEXTURE", request.Field);
        Assert.Equal("pending", request.Status);
        Assert.Equal("MapIcons.Icon", request.Resource);
        Assert.Empty(issues);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void LocalMapContainerReplacesWholeTemplateContainer(bool array)
    {
        var source = TypedSource(array ? "UIMapAreaInfoMetaDataItem" : "UIMapWidgetMetaDataItem");
        var template = TypedSource(source.ExportType);
        var unreachable = new FPropertyTag
        {
            Name = array ? "HeaderImage" : "MapTexture",
            Tag = new SoftObjectProperty(new FSoftObjectPath("NeverLoaded.Image", ""))
        };
        template.Properties.Add(array ? Areas("MapAreas", Structure(unreachable)) : Settings("MapWidgetSettings", unreachable));
        source.Template = new ResolvedLoadedObject(template);
        source.Properties.Add(array ? Areas("MapAreas") : Settings("MapWidgetSettings", Soft("MapTexture")));

        var requests = Images.Capture(source, Mappings.Value, _ => throw new InvalidOperationException("No load expected."), issues);

        if (array) Assert.Empty(requests);
        else Assert.Equal("absent", Assert.Single(requests).Status);
        Assert.Empty(issues);
    }

    [Fact]
    public void MalformedMapAreaDoesNotHideOtherOrdinals()
    {
        var source = TypedSource("UIMapAreaInfoMetaDataItem");
        source.Properties.Add(Areas("MapAreas", new IntProperty(17), Structure(Soft("HeaderImage"))));

        var requests = Images.Capture(source, Mappings.Value, _ => throw new InvalidOperationException("No load expected."), issues);

        Assert.Collection(requests,
            failed => { Assert.Equal("MapAreas[0]", failed.Field); Assert.Equal("failed", failed.Status); },
            absent => { Assert.Equal("MapAreas[1].HeaderImage", absent.Field); Assert.Equal("absent", absent.Status); });
        Assert.Equal("Source.MapAreas[0]", Assert.Single(issues).Path);
    }

    [Theory]
    [InlineData("wrong-root-type")]
    [InlineData("wrong-struct-name")]
    [InlineData("wrong-leaf-type")]
    [InlineData("duplicate-leaf")]
    public void MapImageTypeAndNameAmbiguityRemainExplicit(string fault)
    {
        var source = TypedSource("UIMapWidgetMetaDataItem");
        var root = Settings("MapWidgetSettings", Soft("MapTexture"));
        switch (fault)
        {
            case "wrong-root-type": root.Tag = new IntProperty(3); break;
            case "wrong-struct-name": root.TagData!.StructType = "UnrelatedSettings"; break;
            case "wrong-leaf-type": root = Settings("MapWidgetSettings", new FPropertyTag { Name = "MapTexture", Tag = new IntProperty(3) }); break;
            case "duplicate-leaf": root = Settings("MapWidgetSettings", Soft("MapTexture"), Soft("maptexture")); break;
        }
        source.Properties.Add(root);

        var request = Assert.Single(Images.Capture(source, Mappings.Value, _ => throw new InvalidOperationException("No load expected."), issues));

        Assert.Equal("failed", request.Status);
        Assert.StartsWith("Source.MapWidgetSettings", Assert.Single(issues).Path);
    }

    [Fact]
    public void MapFieldNamesOnUnrelatedRuntimeClassesDoNotCreateRoles()
    {
        var source = TypedSource("Object");
        RuntimeClassFixture.Derive(source, "UIMapAreaInfoMetaDataItem");
        source.Properties.Add(Areas("MapAreas", Structure(Soft("HeaderImage"))));
        source.Properties.Add(Settings("MapWidgetSettings", Soft("MapTexture")));

        Assert.Empty(Images.Capture(source, Mappings.Value, _ => throw new InvalidOperationException("No load expected."), issues));
        Assert.Empty(issues);
    }

    [Theory]
    [InlineData("type")]
    [InlineData("shadow")]
    [InlineData("cycle")]
    public void ChangedNativeMapDeclarationCannotReuseTheOldProjection(string fault)
    {
        var mappings = new TypeMappings(new(Mappings.Value.Types), Mappings.Value.Enums);
        var parent = fault == "shadow" ? "MapParent" : fault == "cycle" ? "MapWidgetLevelSettings" : null;
        mappings.Types["MapWidgetLevelSettings"] = new(mappings, "MapWidgetLevelSettings", null,
            new() { [0] = new(0, "MapTexture", new(fault == "type" ? "IntProperty" : "SoftObjectProperty")) }, 1)
        { SuperType = parent };
        if (fault == "shadow") mappings.Types["MapParent"] = new(mappings, "MapParent", null,
            new() { [0] = new(0, "maptexture", new("SoftObjectProperty")) }, 1);
        var source = TypedSource("UIMapWidgetMetaDataItem");
        source.Properties.Add(Settings("MapWidgetSettings", Soft("MapTexture")));

        Assert.Equal("failed", Assert.Single(Images.Capture(source, mappings,
            _ => throw new InvalidOperationException("No load expected."), issues)).Status);
        Assert.Contains("declaration", Assert.Single(issues).Message);
    }

    private static FPropertyTag Soft(string name, IPackage? package = null) => new()
    {
        Name = name,
        Tag = new SoftObjectProperty(package is null ? default : new FSoftObjectPath("MapIcons.Icon", "", package))
    };

    private static StructProperty Structure(params FPropertyTag[] properties) => new(new FScriptStruct(new FStructFallback(properties.ToList())));

    private static FPropertyTag Areas(string name, params FPropertyTagType[] entries) => new()
    {
        Name = name,
        Tag = new ArrayProperty(new UScriptArray(entries.ToList(), "StructProperty",
            new() { Type = "StructProperty", StructType = "MapAreaInfo" }))
    };

    private static FPropertyTag Settings(string name, params FPropertyTag[] properties) => new()
    {
        Name = name,
        TagData = new() { Type = "StructProperty", StructType = "MapWidgetLevelSettings" },
        Tag = Structure(properties)
    };
}
