using AssetIndex.Discovery;
using CUE4Parse.UE4.Assets;
using CUE4Parse.UE4.Assets.Exports;
using CUE4Parse.UE4.Assets.Exports.Texture;

namespace AssetIndex.Tests;

public sealed class UiTextureSelectionTests
{
    [Fact]
    public void EagerPngExportUsesExactRegistryUiPathsIncludingOuterIdentity()
    {
        var package = new CrawlerPackage("Plugin/Icons.uasset", "/Plugin/Icons");
        var left = new UObject { Name = "Left", Outer = new ResolvedPackageObject(package) };
        var right = new UObject { Name = "Right", Outer = new ResolvedPackageObject(package) };
        var ui = new UTexture2D { Name = "Same", Outer = new ResolvedLoadedObject(left) };
        var sibling = new UTexture2D { Name = "Same", Outer = new ResolvedLoadedObject(right) };
        var ingredient = new UTexture2D { Name = "Ingredient", Outer = new ResolvedPackageObject(package) };
        var registry = new RegisteredObject[]
        {
            new(ui.GetPathName().ToLowerInvariant(), package.Name, "Texture2D", new Dictionary<string, string> { ["LODGroup"] = "TEXTUREGROUP_UI" }),
            new(ingredient.GetPathName(), package.Name, "Texture2D", new Dictionary<string, string> { ["LODGroup"] = "TEXTUREGROUP_World" })
        };
        var discovery = new DiscoveryResult([], [ui, sibling, ingredient], registry, [], [], []);

        Assert.Same(ui, Assert.Single(Program.UiTextures(discovery)));
    }
}
