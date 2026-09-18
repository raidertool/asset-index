using AssetIndex.Discovery;
using CUE4Parse.UE4.Assets.Exports;
using CUE4Parse.UE4.Assets.Objects;
using CUE4Parse.UE4.Assets.Objects.Properties;
using CUE4Parse.UE4.Objects.UObject;

namespace AssetIndex.Tests;

public sealed class CrawlerClassScopeTests
{
    private const string MappingHash = "61018eaea9fed46a1351fbdc5f7429e6f24df8f45fad576b4b15c534b1b359c4";
    private const string Sensing = "/Script/Angelscript.AISensingStatusTransition";

    [Fact]
    public void ProvenNonCatalogFamilyRetainsItsHeaderWithoutReadingTheBody()
    {
        var package = new CrawlerPackage("Plugin/Test.uasset", "/Plugin/Test", Unknown(),
            new("Identity", "PersistenceDataAsset") { OnLoad = source => Integer(source, "AssetId", 42) });
        using var provider = new CrawlerProvider(package) { MappingSha256 = MappingHash };
        var headers = new List<ExportHeader>();
        var evidence = new List<ObjectEvidence>();

        var result = new ObjectCrawler(provider, evidence.Add, headers.Add).Read([], (_, _) => { });

        Assert.Empty(result.Issues);
        Assert.Equal([1], package.BodyReads);
        Assert.Equal(42, Assert.Single(result.Assets).Id);
        Assert.Equal(2, headers.Count);
        Assert.False(headers[0].AncestryComplete);
        Assert.NotNull(headers[0].Error);
        Assert.Equal("/Plugin/Test.Identity", Assert.Single(evidence).Path);
        Assert.Equal(1, result.UnmappedNonCatalogExports);
        Assert.Equal("/Plugin/Test.Unknown", Assert.Single(result.Notices).Path);
        var read = Assert.Single(result.Packages);
        Assert.Equal("succeeded", read.Status);
        Assert.Equal([1], read.Selected);
        Assert.Equal(read.Selected, read.Decoded);
    }

    [Theory]
    [InlineData(Sensing, null)]
    [InlineData(Sensing, "changed")]
    [InlineData("/Script/Fixture.AISensingStatusTransition", MappingHash)]
    [InlineData("/Script/Angelscript.UnknownClass", MappingHash)]
    public void UnprovedMissingClassRemainsFatal(string classPath, string? mappingHash)
    {
        var package = new CrawlerPackage("Plugin/Test.uasset", "/Plugin/Test", Unknown(classPath));
        using var provider = new CrawlerProvider(package) { MappingSha256 = mappingHash };

        var result = new ObjectCrawler(provider, _ => { }, _ => { }).Read([], (_, _) => { });

        Assert.Equal([0], package.BodyReads);
        Assert.Contains(result.Issues, issue => issue.Stage == "metadata");
        Assert.Contains(result.Issues, issue => issue.Stage == "decode" && issue.Message.Contains("Missing fixture layout"));
        Assert.Empty(result.Notices);
        Assert.Equal(0, result.UnmappedNonCatalogExports);
        Assert.Equal("incomplete", Assert.Single(result.Packages).Status);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void IncidentalOrClassDefaultReferencesDoNotReadAProvenNonCatalogBody(bool classDefault)
    {
        CrawlerPackage? package = null;
        package = new("Plugin/Test.uasset", "/Plugin/Test", Unknown(),
            new("Owner", classDefault ? "BlueprintGeneratedClass" : "DataAsset")
            {
                Factory = () => classDefault ? new UClass { ClassDefaultObject = new FPackageIndex(package!, 1) } : new UObject(),
                OnLoad = source => { if (!classDefault) CrawlerExport.Link(source, "/Plugin/Test.Unknown"); }
            });
        using var provider = new CrawlerProvider(package) { MappingSha256 = MappingHash };

        var result = new ObjectCrawler(provider, _ => { }, _ => { }).Read([], (_, _) => { });

        Assert.Empty(result.Issues);
        Assert.Equal([1], package.BodyReads);
        Assert.Single(result.Notices);
        Assert.Equal(1, result.UnmappedNonCatalogExports);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CatalogIdentityOrTemplateCannotUseAnOmittedBody(bool template)
    {
        CrawlerPackage? package = null;
        package = new("Plugin/Test.uasset", "/Plugin/Test", Unknown(),
            new("Definition", template ? "PersistenceDataAsset" : "QuestDefinition")
            {
                OnLoad = source =>
                {
                    if (template) source.Template = package!.ResolvePackageIndex(new FPackageIndex(package, 1));
                    else source.Properties.Add(new FPropertyTag
                    {
                        Name = "PersistenceDataAsset",
                        PropertyType = "ObjectProperty",
                        Tag = new ObjectProperty(new FPackageIndex(package!, 1))
                    });
                }
            });
        using var provider = new CrawlerProvider(package) { MappingSha256 = MappingHash };

        var result = new ObjectCrawler(provider, _ => { }, _ => { }).Read([], (_, _) => { });

        Assert.Contains(0, package.BodyReads);
        Assert.Contains(result.Issues, issue => issue.Stage == "asset" && issue.Message.Contains("Missing fixture layout"));
        Assert.Empty(result.Assets);
    }

    private static CrawlerExport Unknown(string classPath = Sensing) => new("Unknown", classPath[(classPath.LastIndexOf('.') + 1)..])
    {
        ClassPath = classPath,
        Factory = () => throw new InvalidDataException("Missing fixture layout.")
    };

    private static void Integer(UObject source, string name, long value) => source.Properties.Add(new FPropertyTag
    {
        Name = name,
        PropertyType = "Int64Property",
        Tag = new Int64Property(value)
    });
}
