using AssetIndex.Discovery;
using CUE4Parse.UE4.Assets;
using CUE4Parse.UE4.Assets.Objects.Properties;
using CUE4Parse.UE4.IO.Objects;
using CUE4Parse.UE4.Objects.UObject;
using static AssetIndex.Tests.ArrayEvidenceFixture;

namespace AssetIndex.Tests;

public sealed class ImportDiagnosticsTests
{
    [Fact]
    public void AFailedImportRetainsTheExactHashAndAlreadyResolvedCandidateHeaders()
    {
        var winner = Package(exports: 1);
        winner.Name = "/Game/Imported";
        var older = Package(exports: 2);
        older.Name = "/Game/Imported";
        var owner = Import(0, 0, [0xFEDCBA9876543210], new(() => [winner]), new(() => [[older]]));

        var evidence = Read(new ObjectProperty(new FPackageIndex(owner, -1)));

        var reference = Assert.Single(evidence.References, value => value.Role == "property");
        Assert.False(reference.IsNull);
        Assert.Null(reference.TargetPath);
        Assert.Equal(-1, reference.PackageIndex);
        Assert.Contains("mapSlot=0; objectIndex=0x8000000000000000; type=PackageImport", reference.Error);
        Assert.Contains("packageSlot=0; hashSlot=0; expectedHash=0xFEDCBA9876543210", reference.Error);
        Assert.Contains("packageIdentity=unavailable(untracked IoStore source)", reference.Error);
        Assert.Contains("winner={name=\"/Game/Imported\",exports=1}", reference.Error);
        Assert.Contains("alternatives=[{name=\"/Game/Imported\",exports=2}]", reference.Error);
        Assert.All(winner.ExportsLazy.Concat(older.ExportsLazy), export => Assert.False(export.IsValueCreated));
        Assert.Empty(evidence.Issues);
    }

    [Fact]
    public void ALoadedNullCandidateIsDistinguishedFromAnUnevaluatedOne()
    {
        var owner = Import(0, 0, [123], new(() => [null]), new(() => [[]]));
        _ = owner.ImportedPackages.Value;
        _ = owner.ImportedPackagesAllVersions.Value;

        var error = ImportDiagnostics.Describe(new(owner, -1), "Unresolved.");

        Assert.Contains("winner=null; alternatives=[]", error);
    }

    [Fact]
    public void DiagnosticsNeverEvaluateAnUncreatedOrFaultedImportLazy()
    {
        var loads = 0;
        var owner = Import(0, 0, [123], new(() =>
        {
            loads++;
            throw new InvalidDataException("Header read failed.");
        }), new(() => throw new InvalidOperationException("Alternative packages must not load.")));

        var before = ImportDiagnostics.Describe(new(owner, -1), "Unresolved.");
        Assert.Equal(0, loads);
        Assert.Contains("winner=unavailable; alternatives=unavailable", before);
        Assert.Throws<InvalidDataException>(() => owner.ImportedPackages.Value);
        Assert.Equal(before, ImportDiagnostics.Describe(new(owner, -1), "Unresolved."));
        Assert.Equal(1, loads);
    }

    [Theory]
    [InlineData(-2, 1L)]
    [InlineData(int.MinValue, 2147483647L)]
    public void ImportMapBoundsPreserveTheOriginalFailure(int index, long slot)
    {
        var owner = Import(0, 0, [123], new(() => throw new Exception()), new(() => throw new Exception()));

        var error = ImportDiagnostics.Describe(new(owner, index), "Original failure.");

        Assert.Equal($"Original failure. Io import: mapSlot={slot} outside importMapLength=1.", error);
    }

    [Theory]
    [InlineData(0U, 1U, "expectedHash=out-of-range(length=1)")]
    [InlineData(1U, 0U, "winner=out-of-range(length=1); alternatives=out-of-range(length=1)")]
    public void HashAndPackageBoundsDoNotHideTheOriginalFailure(uint packageSlot, uint hashSlot, string expected)
    {
        var owner = Import(packageSlot, hashSlot, [123], new(() => [null]), new(() => [[]]));
        _ = owner.ImportedPackages.Value;
        _ = owner.ImportedPackagesAllVersions.Value;

        var error = ImportDiagnostics.Describe(new(owner, -1), "Original failure.");

        Assert.StartsWith("Original failure.", error);
        Assert.Contains(expected, error);
    }

    [Fact]
    public void NonIoAndNonImportReferencesKeepTheirExistingDiagnostics()
    {
        Assert.Equal("Original.", ImportDiagnostics.Describe(new(new FixturePackage(), -1), "Original."));
        Assert.Equal("Original.", ImportDiagnostics.Describe(new(Package(), 1), "Original."));
        Assert.Equal("Original.", ImportDiagnostics.Describe(new(), "Original."));
    }

    private static IoPackage Package(int exports = 0) => IoMetadataFixture.Create(
        Enumerable.Range(0, exports).Select(index => IoMetadataFixture.Entry(index, FPackageObjectIndex.Invalid)).ToArray(),
        Enumerable.Range(0, exports).Select(index => "Export" + index).ToArray(), [],
        () => throw new InvalidOperationException("Diagnostics must not decode bodies."));

    private static IoPackage Import(uint packageSlot, uint hashSlot, ulong[] hashes,
        Lazy<IoPackage?[]> packages, Lazy<IPackage?[][]> alternatives)
    {
        var index = new FPackageObjectIndex((2UL << 62) | ((ulong)packageSlot << 32) | hashSlot);
        var owner = IoMetadataFixture.Create([], [], [], () => throw new Exception(), [index]);
        Set(owner, nameof(IoPackage.ImportedPublicExportHashes), hashes);
        Set(owner, nameof(IoPackage.ImportedPackages), packages);
        Set(owner, nameof(IoPackage.ImportedPackagesAllVersions), alternatives);
        return owner;
    }

    private static void Set(IoPackage owner, string field, object value) =>
        typeof(IoPackage).GetField(field)!.SetValue(owner, value);
}
