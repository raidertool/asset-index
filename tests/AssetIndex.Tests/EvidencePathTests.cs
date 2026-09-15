using AssetIndex.Discovery;
using CUE4Parse.MappingsProvider;
using CUE4Parse.UE4.Assets;
using CUE4Parse.UE4.Assets.Exports;
using CUE4Parse.UE4.Assets.Objects;
using CUE4Parse.UE4.Assets.Objects.Properties;
using CUE4Parse.UE4.IO.Objects;
using CUE4Parse.UE4.Objects.UObject;
using static AssetIndex.Tests.IoMetadataFixture;

namespace AssetIndex.Tests;

public sealed class EvidencePathTests
{
    [Theory]
    [InlineData("valid", null)]
    [InlineData("missing-outer", "no package root")]
    [InlineData("none-name", "empty name")]
    public void NativeReferencesNeedValidPathsEvenWhenExportHeadersAreComplete(string kind, string? error)
    {
        var script = 1UL << FPackageObjectIndex.TypeShift;
        var loaded = 0;
        var package = Create([Entry(3, script | 2)],
            ["/Script/Fixture", "DataAsset", kind == "none-name" ? "None" : "Target", "Source"],
            new()
            {
                [new(script | 1)] = ScriptEntry(0, script | 1, FPackageObjectIndex.Invalid),
                [new(script | 2)] = ScriptEntry(1, script | 2, script | 1),
                [new(script | 3)] = ScriptEntry(2, script | 3, kind == "missing-outer" ? script | 99 : script | 1)
            }, () => loaded++, [new(script | 3)]);
        var source = new UObject([new FPropertyTag
        {
            Name = "RelatedNative", PropertyType = "ObjectProperty", Tag = new ObjectProperty(new FPackageIndex(package, -1))
        }])
        {
            Name = "Source",
            Outer = new ResolvedPackageObject(package),
            Class = package.ResolveObjectIndex(new(script | 2)),
            Template = package.ResolveObjectIndex(new(script | 3))
        };
        var mappings = new TypeMappings();
        mappings.Types.Add("Object", new(mappings, "Object", null, [], 0));
        mappings.Types.Add("DataAsset", new(mappings, "DataAsset", "Object", [], 0));

        var header = Assert.Single(ExportInventory.Read(package, "Map.uasset", mappings));
        var evidence = EvidenceReader.Read(source);

        Assert.True(header.AncestryComplete);
        Assert.Null(header.Error);
        Assert.Equal(header.Path, evidence.Path);
        Assert.Empty(evidence.Issues);
        var targets = evidence.References.Where(reference => reference.Pointer is "/Template" or "/Properties/0").ToArray();
        Assert.Equal(2, targets.Length);
        Assert.All(targets, reference =>
        {
            Assert.False(reference.IsNull);
            if (error is null)
            {
                Assert.Equal("/Script/Fixture.Target", reference.TargetPath);
                Assert.Null(reference.Error);
            }
            else
            {
                Assert.Null(reference.TargetPath);
                Assert.Contains(error, reference.Error);
            }
        });
        Assert.Equal(0, loaded);
    }
}
