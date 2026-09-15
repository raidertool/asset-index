using AssetIndex.Discovery;
using CUE4Parse.MappingsProvider;
using CUE4Parse.UE4.Assets.Exports;
using CUE4Parse.UE4.Objects.Engine;
using CUE4Parse.UE4.Objects.UObject;

namespace AssetIndex.Tests;

public sealed class SchemaDependencyTests
{
    [Fact]
    public void ReferencedRuntimeDeclarationsEmitTheirBodiesAndNestedDependencies()
    {
        CrawlerPackage? package = null;
        UStruct? implicitlyLoaded = null;
        package = new CrawlerPackage("Plugin/Schema.uasset", "/Plugin/Schema",
            new CrawlerExport("Definition", "BlueprintGeneratedClass")
            {
                Factory = () => new UBlueprintGeneratedClass { ChildProperties = [] },
                OnLoad = source =>
                {
                    var field = new FStructProperty { Name = "Details", ArrayDim = 1, Struct = new(package!, 2) };
                    ((UStruct)source).ChildProperties = [field];
                    // SerializedStruct uses this CUE constructor while decoding runtime fields.
                    implicitlyLoaded = new PropertyType(field).Struct;
                    CrawlerExport.Link(source, "/Plugin/Schema.Actor");
                }
            },
            new CrawlerExport("DetailsType", "UserDefinedStruct")
            {
                Factory = () => new UUserDefinedStruct { ChildProperties = [] },
                OnLoad = source => ((UStruct)source).ChildProperties =
                    [new FObjectProperty { Name = "Kind", ArrayDim = 1, PropertyClass = new(package!, 3) }]
            },
            new CrawlerExport("DeclaredClass", "Class") { Factory = () => new UClass { ChildProperties = [] } },
            new CrawlerExport("UnrelatedStruct", "UserDefinedStruct"),
            new CrawlerExport("Actor", "Actor"));
        using var provider = new CrawlerProvider(package);
        var evidence = new List<ObjectEvidence>();
        var headers = new List<ExportHeader>();

        var result = new ObjectCrawler(provider, evidence.Add, headers.Add).Read([], (_, _) => { });

        Assert.IsType<UUserDefinedStruct>(implicitlyLoaded);
        Assert.Empty(result.Issues);
        Assert.Equal([0, 1, 2], package.BodyReads);
        Assert.Equal(5, headers.Count);
        Assert.All(headers, header => Assert.Null(header.Error));
        Assert.Equal(["/Plugin/Schema.Definition", "/Plugin/Schema.DetailsType", "/Plugin/Schema.DeclaredClass"],
            evidence.Select(row => row.Path));
        Assert.Contains(evidence[0].References, reference => reference.Pointer == "/Native/ChildProperties/0/Struct"
            && reference.TargetPath == "/Plugin/Schema.DetailsType");
        Assert.Contains(evidence[1].References, reference => reference.Pointer == "/Native/ChildProperties/0/PropertyClass"
            && reference.TargetPath == "/Plugin/Schema.DeclaredClass");
        var read = Assert.Single(result.Packages);
        Assert.Equal("succeeded", read.Status);
        Assert.Equal([0, 1, 2], read.Selected);
        Assert.Equal(read.Selected, read.Decoded);
    }
}
