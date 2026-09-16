using AssetIndex.Discovery;

namespace AssetIndex.Tests;

public sealed class ExportSchedulingTests
{
    [Fact]
    public void RegisteredNoncandidatePackagesStillExposeInlineCandidateExports()
    {
        var map = new CrawlerPackage("Plugin/Map.umap", "/Plugin/Map", new CrawlerExport("Map", "World"), new CrawlerExport("Inline"));
        using var provider = new CrawlerProvider(map);
        var headers = new List<ExportHeader>();

        var result = new ObjectCrawler(provider, _ => { }, headers.Add).Read([Registered("/Plugin/Map.Map", "World")], (_, _) => { });

        Assert.Empty(result.Issues);
        Assert.Equal(2, headers.Count);
        Assert.Equal([1], map.BodyReads);
        Assert.Equal("inventory", Assert.Single(result.Packages).Reason);
    }

    [Fact]
    public void UnindexedPackagesRetainEveryHeaderAndDecodeOnlyCandidateBodies()
    {
        var headers = new List<ExportHeader>();
        var map = new CrawlerPackage("Plugin/Map.umap", "/Plugin/Map",
            new CrawlerExport("Map", "World"), new CrawlerExport("Spawner", "Actor"), new CrawlerExport("Inline")
            {
                OnLoad = _ => Assert.Equal(3, headers.Count)
            });
        using var provider = new CrawlerProvider(map);

        var result = new ObjectCrawler(provider, _ => { }, headers.Add).Read([], (_, _) => { });

        Assert.Empty(result.Issues);
        Assert.Equal([2], map.BodyReads);
        var package = Assert.Single(result.Packages);
        Assert.Equal("/Plugin/Map", package.Name);
        Assert.Equal("succeeded", package.Status);
        Assert.Equal(3, package.Exports);
        Assert.Equal([2], package.Selected);
        Assert.Equal([2], package.Decoded);
    }

    [Fact]
    public void ExactSubobjectReferencesDoNotLoadSiblingsAndKeepInlineCandidates()
    {
        var source = new CrawlerPackage("Plugin/Source.uasset", "/Plugin/Source", new CrawlerExport("Source")
        {
            OnLoad = value => CrawlerExport.Link(value, "/plugin/map.map:Left.Icon")
        });
        var map = new CrawlerPackage("Plugin/Map.umap", "/Plugin/Map", new CrawlerExport("Map", "World"),
            new CrawlerExport("Left", "Actor") { OuterIndex = 0 }, new CrawlerExport("Right", "Actor") { OuterIndex = 0 },
            new CrawlerExport("Icon", "Texture2D") { OuterIndex = 1 }, new CrawlerExport("Icon", "Texture2D") { OuterIndex = 2 }, new CrawlerExport("Inline"));
        using var provider = new CrawlerProvider(source, map);

        var result = new ObjectCrawler(provider, _ => { }, _ => { }).Read(
            [Registered("/Plugin/Source.Source"), Registered("/Plugin/Map.Map", "World")], (_, _) => { });

        Assert.Empty(result.Issues);
        Assert.Equal([3, 5], map.BodyReads.Order());
        Assert.Equal([3, 5], Assert.Single(result.Packages, package => package.Name == "/Plugin/Map").Selected);
        Assert.Contains(result.Objects, value => value.Path == "/Plugin/Map.Inline");
    }

    [Fact]
    public void KnownNonfollowTargetsAreResolvedFromHeadersWithoutDecodingTheirBodies()
    {
        var source = new CrawlerPackage("Plugin/Source.uasset", "/Plugin/Source", new CrawlerExport("Source")
        {
            OnLoad = value => CrawlerExport.Link(value, "/Plugin/Map.Map:Actor")
        });
        var map = new CrawlerPackage("Plugin/Map.umap", "/Plugin/Map", new CrawlerExport("Map", "World"),
            new CrawlerExport("Actor", "Actor") { OuterIndex = 0 });
        using var provider = new CrawlerProvider(source, map);

        var result = new ObjectCrawler(provider, _ => { }, _ => { }).Read(
            [Registered("/Plugin/Source.Source"), Registered("/Plugin/Map.Map", "World")], (_, _) => { });

        Assert.Empty(result.Issues);
        Assert.Empty(map.BodyReads);
        Assert.Empty(Assert.Single(result.Packages, package => package.Name == "/Plugin/Map").Selected);
    }

    [Fact]
    public void ALoadedPackageCannotHideAMissingFullReferenceTarget()
    {
        var source = new CrawlerPackage("Plugin/Source.uasset", "/Plugin/Source", new CrawlerExport("Source")
        {
            OnLoad = value => CrawlerExport.Link(value, "/Plugin/Map.Map:Missing")
        });
        var map = new CrawlerPackage("Plugin/Map.umap", "/Plugin/Map", new CrawlerExport("Map", "World"));
        using var provider = new CrawlerProvider(source, map);

        var result = new ObjectCrawler(provider, _ => { }, _ => { }).Read([], (_, _) => { });

        Assert.Contains(result.Issues, issue => issue.Stage == "reference" && issue.Message.Contains("found 0"));
        Assert.Empty(map.BodyReads);
        Assert.Equal("incomplete", Assert.Single(result.Packages, package => package.Name == "/Plugin/Map").Status);
    }

    [Theory]
    [InlineData("NewUnmappedClass", false)]
    [InlineData("DataAsset", true)]
    public void UnknownOrMissingMetadataIsAttemptedAndBlocksCompleteCoverage(string type, bool missing)
    {
        var package = new CrawlerPackage("Plugin/Unknown.uasset", "/Plugin/Unknown", new CrawlerExport("Unknown", type));
        if (missing) package.MissingMetadata = 0;
        using var provider = new CrawlerProvider(package);
        var headers = new List<ExportHeader>();

        var result = new ObjectCrawler(provider, _ => { }, headers.Add).Read([], (_, _) => { });

        Assert.Equal([0], package.BodyReads);
        Assert.NotNull(Assert.Single(headers).Error);
        Assert.Contains(result.Issues, issue => issue.Stage == "metadata");
        Assert.Equal("incomplete", Assert.Single(result.Packages).Status);
    }

    [Theory]
    [InlineData(null, false)]
    [InlineData("/Plugin/BP.BP_C", true)]
    [InlineData("BlueprintGeneratedClass'/Plugin/BP.BP_C'", true)]
    [InlineData("Texture2D'/Plugin/BP.BP_C'", false)]
    [InlineData("BlueprintGeneratedClass'/Plugin/BP.Missing'", false)]
    public void CookedBlueprintCorrespondenceRequiresTheExplicitValidatedTag(string? generated, bool matched)
    {
        var package = new CrawlerPackage("Plugin/BP.uasset", "/Plugin/BP", new CrawlerExport("BP_C", "BlueprintGeneratedClass"));
        using var provider = new CrawlerProvider(package);
        var tags = new Dictionary<string, string>();
        if (generated is not null) tags.Add("GeneratedClass", generated);
        var registry = new RegisteredObject("/Plugin/BP.BP", "/Plugin/BP", "Blueprint", tags);

        var result = new ObjectCrawler(provider, _ => { }, _ => { }).Read([registry], (_, _) => { });

        Assert.Equal(matched, !result.Issues.Any(issue => issue.Stage == "registry"));
        Assert.Equal([0], package.BodyReads);
    }

    [Fact]
    public void SelectedRegistryRowsCannotDisappearInANonemptyPackage()
    {
        using var provider = new CrawlerProvider(new CrawlerPackage("Plugin/Asset.uasset", "/Plugin/Asset", new CrawlerExport("Actual")));

        var result = new ObjectCrawler(provider, _ => { }, _ => { }).Read([Registered("/Plugin/Asset.Expected")], (_, _) => { });

        Assert.Contains(result.Issues, issue => issue.Stage == "registry" && issue.Path == "/Plugin/Asset.Expected");
        Assert.Equal("incomplete", Assert.Single(result.Packages).Status);
    }

    [Fact]
    public void EmptyUnindexedPackagesStillHaveAnInspectionAndLogicalIdentity()
    {
        var empty = new CrawlerPackage("Plugin/Empty.umap", "/Plugin/Empty");
        using var provider = new CrawlerProvider(empty);

        var result = new ObjectCrawler(provider, _ => { }, _ => { }).Read([], (_, _) => { });

        Assert.Equal([empty.PhysicalPath], provider.PackageReads);
        var read = Assert.Single(result.Packages);
        Assert.Equal("/Plugin/Empty", read.Name);
        Assert.Equal(0, read.Exports);
        Assert.Empty(read.Selected);
        Assert.Empty(read.Decoded);
    }

    [Fact]
    public void FailedSelectedExportsRemainAccountedAndDoNotDecodeOtherClasses()
    {
        var package = new CrawlerPackage("Plugin/Asset.uasset", "/Plugin/Asset",
            new CrawlerExport("Broken") { OnLoad = _ => throw new InvalidDataException("Deliberate body failure.") }, new CrawlerExport("World", "World"));
        using var provider = new CrawlerProvider(package);

        var result = new ObjectCrawler(provider, _ => { }, _ => { }).Read([], (_, _) => { });

        var read = Assert.Single(result.Packages);
        Assert.Equal([0], read.Selected);
        Assert.Empty(read.Decoded);
        Assert.Equal([0], package.BodyReads);
        Assert.Equal("incomplete", read.Status);
        Assert.Contains(result.Issues, issue => issue.Stage == "decode");
    }

    [Fact]
    public void MetadataAndDecodedObjectIdentityMustAgree()
    {
        var package = new CrawlerPackage("Plugin/Asset.uasset", "/Plugin/Asset", new CrawlerExport("Expected") { OnLoad = value => value.Name = "Different" });
        using var provider = new CrawlerProvider(package);

        var result = new ObjectCrawler(provider, _ => { }, _ => { }).Read([], (_, _) => { });

        Assert.Contains(result.Issues, issue => issue.Stage == "metadata" && issue.Message.Contains("disagrees"));
    }

    private static RegisteredObject Registered(string path, string type = "DataAsset") =>
        new(path, path.Split('.')[0], type, new Dictionary<string, string>());
}
