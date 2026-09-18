using AssetIndex.Discovery;
using CUE4Parse.UE4.Assets;
using CUE4Parse.UE4.Assets.Exports;
using CUE4Parse.UE4.Objects.UObject;

namespace AssetIndex.Tests;

public sealed class CrawlerClassDeclarationTests
{
    private const string Metaclass = "/Script/UMG.WidgetBlueprintGeneratedClass";
    private const string NativeParent = "/Script/Fixture.UnmappedWidget";

    [Theory]
    [InlineData("valid")]
    [InlineData("qualified-class")]
    [InlineData("superclass")]
    [InlineData("missing-superclass")]
    [InlineData("decode-failure")]
    public void WidgetOmissionStillRequiresItsDeclarationBodyToMatchTheHeader(string mutation)
    {
        var package = new CrawlerPackage("Plugin/Panel.uasset", "/Plugin/Panel",
            new("Panel_C", "WidgetBlueprintGeneratedClass")
            {
                ClassPath = Metaclass,
                SuperPath = NativeParent,
                Factory = () => mutation == "decode-failure"
                    ? throw new InvalidDataException("Declaration could not be decoded.")
                    : new UClass
                    {
                        ChildProperties = [],
                        SuperStruct = mutation == "missing-superclass" ? null! : NativeIndex(
                            mutation == "superclass" ? "/Script/Different.UnmappedWidget" : NativeParent)
                    },
                OnLoad = source =>
                {
                    if (mutation == "qualified-class")
                        source.Class = NativeIndex("/Script/Different.WidgetBlueprintGeneratedClass").ResolvedObject;
                }
            },
            new("Panel", "Panel_C")
            {
                ClassIndex = 0,
                Factory = () => throw new InvalidDataException("Unknown widget body must remain unread.")
            });
        using var provider = new CrawlerProvider(package);
        var headers = new List<ExportHeader>();

        var result = new ObjectCrawler(provider, _ => { }, headers.Add).Read([], (_, _) => { });

        Assert.Equal([0], package.BodyReads);
        Assert.Equal(2, headers.Count);
        Assert.False(headers[1].AncestryComplete);
        Assert.Equal(1, result.UnmappedNonCatalogExports);
        if (mutation == "valid")
        {
            Assert.Empty(result.Issues);
            Assert.Equal("succeeded", Assert.Single(result.Packages).Status);
        }
        else
        {
            var stage = mutation == "decode-failure" ? "decode" : "metadata";
            Assert.Contains(result.Issues, issue => issue.Stage == stage && issue.Path.EndsWith("#export/0", StringComparison.Ordinal));
            Assert.Equal("incomplete", Assert.Single(result.Packages).Status);
        }
    }

    private static FPackageIndex NativeIndex(string path)
    {
        var split = path.LastIndexOf('.');
        var native = new UScriptClass(path[(split + 1)..])
        {
            Outer = new ResolvedPackageObject(new FixturePackage { Name = path[..split] })
        };
        return new(new FixturePackage(native), 1);
    }
}
