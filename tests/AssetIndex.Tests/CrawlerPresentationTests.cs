using AssetIndex.Discovery;
using CUE4Parse.UE4.Assets.Exports;
using CUE4Parse.UE4.Assets.Objects;
using CUE4Parse.UE4.Assets.Objects.Properties;
using CUE4Parse.UE4.Objects.UObject;

namespace AssetIndex.Tests;

public sealed class CrawlerPresentationTests
{
    [Fact]
    public void WidgetClassDefaultsAndOwnedTreeBodiesAreDecodedOnceDespiteCycles()
    {
        CrawlerPackage? package = null;
        package = new("Plugin/Panel.uasset", "/Plugin/Panel",
            new("PanelClass", "WidgetBlueprintGeneratedClass")
            {
                Factory = () => new UClass { ClassDefaultObject = new FPackageIndex(package!, 2) },
                OnLoad = source => Link(source, "WidgetTree", package!, 3)
            },
            new("Default", "UserWidget") { OnLoad = source => Link(source, "WidgetTree", package!, 3) },
            new("Tree", "WidgetTree") { OuterIndex = 0, OnLoad = source => Link(source, "RootWidget", package!, 4) },
            new("Root", "Overlay") { OuterIndex = 2, OnLoad = source => Link(source, "Slot", package!, 5) },
            new("Slot", "OverlaySlot")
            {
                OuterIndex = 3,
                OnLoad = source => { Link(source, "Parent", package!, 4); Link(source, "Content", package!, 6); }
            },
            new("Label", "TextBlock") { OuterIndex = 2, OnLoad = source => CrawlerExport.Link(source, "/Plugin/Panel.Unrelated") },
            new("Unrelated", "Actor"), new("Unreferenced", "TextBlock"));
        using var provider = new CrawlerProvider(package);
        var evidence = new List<ObjectEvidence>();

        var result = new ObjectCrawler(provider, evidence.Add, _ => { }).Read([], (_, _) => { });

        Assert.Empty(result.Issues);
        Assert.Equal(Enumerable.Range(0, 6), package.BodyReads.Order());
        Assert.Equal(6, evidence.Count);
        Assert.Contains(evidence, item => item.Path == "/Plugin/Panel.PanelClass:Tree.Label");
        Assert.Equal("succeeded", Assert.Single(result.Packages).Status);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void ExactDefaultsAndTemplatesPromoteEarlierInventoryTargetsAndFailClosed(bool template, bool failBody)
    {
        CrawlerPackage? package = null;
        package = new("Plugin/Definition.uasset", "/Plugin/Definition",
            new("Ordinary") { OnLoad = source => CrawlerExport.Link(source, "/Plugin/Definition.Default") },
            new("Owner", template ? "DataAsset" : "BlueprintGeneratedClass")
            {
                Factory = () => template ? new UObject() : new UClass { ClassDefaultObject = new FPackageIndex(package!, 3) },
                OnLoad = source => { if (template) source.Template = package!.ResolvePackageIndex(new FPackageIndex(package, 3)); }
            },
            new("Default", "Actor")
            {
                Factory = () => failBody ? throw new InvalidDataException("Default body cannot be decoded.") : new UObject(),
                OnLoad = source => source.Template = package!.ResolvePackageIndex(new FPackageIndex(package, 2))
            },
            new("Unrelated", "Actor"));
        using var provider = new CrawlerProvider(package);

        var result = new ObjectCrawler(provider, _ => { }, _ => { }).Read([], (_, _) => { });

        Assert.Equal([0, 1, 2], package.BodyReads);
        var read = Assert.Single(result.Packages);
        Assert.Equal([0, 1, 2], read.Selected);
        if (failBody)
        {
            Assert.Equal([0, 1], read.Decoded);
            Assert.Equal("incomplete", read.Status);
            Assert.Contains(result.Issues, issue => issue.Stage == "decode" && issue.Message.Contains("Default body cannot be decoded"));
            Assert.Contains(result.Issues, issue => issue.Stage == "reference" && issue.Message.Contains("Default"));
        }
        else
        {
            Assert.Equal(read.Selected, read.Decoded);
            Assert.Equal("succeeded", read.Status);
            Assert.Empty(result.Issues);
        }
    }

    private static void Link(UObject source, string name, CrawlerPackage package, int index) =>
        source.Properties.Add(new FPropertyTag
        {
            Name = name,
            PropertyType = "ObjectProperty",
            Tag = new ObjectProperty(new FPackageIndex(package, index))
        });
}
