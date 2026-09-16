using AssetIndex.Discovery;
using CUE4Parse.UE4.Assets.Exports.Texture;

namespace AssetIndex.Tests;

public sealed class UiTextureSelectionTests
{
    [Fact]
    public void EagerPngExportUsesExactRegistryUiPathsIncludingOuterIdentity()
    {
        var package = new CrawlerPackage("Plugin/Icons.uasset", "/Plugin/Icons",
            new CrawlerExport("Left", "Object"),
            new CrawlerExport("Right", "Object"),
            new CrawlerExport("Same", "Texture2D")
            {
                OuterIndex = 0,
                Factory = () => new UTexture2D(),
                OnLoad = source => CrawlerExport.Link(source, "/Plugin/Icons.Ingredient")
            },
            new CrawlerExport("Same", "Texture2D") { OuterIndex = 1, Factory = () => new UTexture2D() },
            new CrawlerExport("Ingredient", "Texture2D") { Factory = () => new UTexture2D() });
        using var provider = new CrawlerProvider(package);
        var registry = new RegisteredObject[]
        {
            new("/plugin/icons.left:same", package.Name, "Texture2D", new Dictionary<string, string> { ["LODGroup"] = "TEXTUREGROUP_UI" }),
            new("/Plugin/Icons.Ingredient", package.Name, "Texture2D", new Dictionary<string, string> { ["LODGroup"] = "TEXTUREGROUP_World" })
        };
        var discovery = new ObjectCrawler(provider, _ => { }, _ => { }).Read(registry, (_, _) => { });

        var location = Assert.Single(discovery.UiTextures);
        Assert.Equal("/Plugin/Icons.Left:Same", location.Path);
        Assert.Equal(2, location.ExportIndex);
        Assert.Same(provider[package.PhysicalPath], location.File);
        Assert.Contains(discovery.Objects, source => source.Path == "/Plugin/Icons.Ingredient");
        Assert.DoesNotContain(3, package.BodyReads);
        Assert.Empty(discovery.Issues);
    }
}
