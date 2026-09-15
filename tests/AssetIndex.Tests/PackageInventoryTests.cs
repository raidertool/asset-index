using AssetIndex.Discovery;
using CUE4Parse.Compression;
using CUE4Parse.FileProvider.Objects;
using CUE4Parse.GameTypes.Theia.FileProvider;
using CUE4Parse.UE4.Assets.Objects;
using CUE4Parse.UE4.IO.Objects;
using CUE4Parse.UE4.Readers;
using CUE4Parse.UE4.Versions;

namespace AssetIndex.Tests;

public sealed class PackageInventoryTests
{
    [Fact]
    public void PackageIdsResolveVirtualRootsWithoutFilenameGuesses()
    {
        using var provider = Provider();
        var cooked = new FixtureFile("PioneerGame/Intermediate/CookedOnly/Actual.uasset");
        var unindexed = new FixtureFile("PioneerGame/Content/Unindexed.uasset");
        var payload = new FixtureFile("PioneerGame/Content/Unindexed.uexp");
        provider.Files.AddFiles(Files(cooked, unindexed, payload), packageFiles: new Dictionary<FPackageId, GameFile>
        {
            [FPackageId.FromName("/RuntimeOnly/RegistryName")] = cooked
        });
        var issues = new List<ExtractionIssue>();

        var inventory = PackageInventory.Read(provider, [Registered("/RuntimeOnly/RegistryName")], issues);

        Assert.Empty(issues);
        Assert.Equal(2, inventory.Count);
        Assert.Equal(["/RuntimeOnly/RegistryName"], Assert.Single(inventory, file => file.Path == cooked.Path).RegistryPackages);
        Assert.Empty(Assert.Single(inventory, file => file.Path == unindexed.Path).RegistryPackages);
    }

    [Fact]
    public void NormalMountResolutionWinsOverThePackageIdFallback()
    {
        using var provider = Provider();
        var normal = new FixtureFile("Plugin/Target.uasset");
        var fallback = new FixtureFile("PioneerGame/Intermediate/Target.uasset");
        provider.Files.AddFiles(Files(normal, fallback), packageFiles: new Dictionary<FPackageId, GameFile>
        {
            [FPackageId.FromName("/Plugin/Target")] = fallback
        });
        var issues = new List<ExtractionIssue>();

        var inventory = PackageInventory.Read(provider, [Registered("/Plugin/Target")], issues);

        Assert.Empty(issues);
        Assert.Equal(["/Plugin/Target"], Assert.Single(inventory, file => file.Path == normal.Path).RegistryPackages);
        Assert.Empty(Assert.Single(inventory, file => file.Path == fallback.Path).RegistryPackages);
    }

    [Fact]
    public void ShadowedArchiveVersionsCountOnceWithTheWinningPathsCasing()
    {
        using var provider = Provider();
        var original = new FixtureFile("Plugin/Target.uasset");
        var current = new FixtureFile("Plugin/TARGET.uasset");
        provider.Files.AddFiles(Files(original), readOrder: 1);
        provider.Files.AddFiles(Files(current), readOrder: 2);
        var issues = new List<ExtractionIssue>();

        var inventory = PackageInventory.Read(provider, [Registered("/Plugin/Target")], issues);

        Assert.Empty(issues);
        Assert.Equal(current.Path, Assert.Single(inventory).Path);
    }

    [Fact]
    public void MultipleRegistryObjectsAndAliasesShareOnePhysicalInput()
    {
        using var provider = Provider();
        var physical = new FixtureFile("PioneerGame/Intermediate/Actual.uasset");
        provider.Files.AddFiles(Files(physical), packageFiles: new Dictionary<FPackageId, GameFile>
        {
            [FPackageId.FromName("/RuntimeOnly/First")] = physical,
            [FPackageId.FromName("/RuntimeOnly/Second")] = physical
        });
        var registry = new[] { Registered("/RuntimeOnly/Second"), Registered("/RuntimeOnly/First"), Registered("/runtimeonly/first") };
        var issues = new List<ExtractionIssue>();

        var inventory = PackageInventory.Read(provider, registry, issues);

        Assert.Empty(issues);
        Assert.Equal(["/RuntimeOnly/First", "/RuntimeOnly/Second"], Assert.Single(inventory).RegistryPackages);
    }

    [Fact]
    public void MissingRegistryPackagesAreReportedWithoutInventingInputs()
    {
        using var provider = Provider();
        provider.Files.AddFiles(Files(new FixtureFile("Plugin/Existing.uasset")));
        var issues = new List<ExtractionIssue>();

        var inventory = PackageInventory.Read(provider, [Registered("/Plugin/Missing")], issues);

        Assert.Empty(Assert.Single(inventory).RegistryPackages);
        Assert.Equal("inventory", Assert.Single(issues).Stage);
        Assert.Equal("/Plugin/Missing", issues[0].Path);
    }

    [Fact]
    public void NoRegistryEntriesLeaveEveryMountedPackageUnindexed()
    {
        using var provider = Provider();
        provider.Files.AddFiles(Files(new FixtureFile("Plugin/First.uasset"), new FixtureFile("Plugin/Second.umap")));
        var issues = new List<ExtractionIssue>();

        var inventory = PackageInventory.Read(provider, [], issues);

        Assert.Empty(issues);
        Assert.Equal(2, inventory.Count);
        Assert.All(inventory, file => Assert.Empty(file.RegistryPackages));
    }

    [Fact]
    public void AMapAndAssetWithTheSameStemStayDistinct()
    {
        using var provider = Provider();
        var asset = new FixtureFile("Plugin/Target.uasset");
        var map = new FixtureFile("Plugin/Target.umap");
        provider.Files.AddFiles(Files(map, asset));
        var issues = new List<ExtractionIssue>();

        var inventory = PackageInventory.Read(provider, [Registered("/Plugin/Target")], issues);

        Assert.Empty(issues);
        Assert.Equal(2, inventory.Count);
        Assert.Equal(["/Plugin/Target"], Assert.Single(inventory, file => file.Path == asset.Path).RegistryPackages);
        Assert.Empty(Assert.Single(inventory, file => file.Path == map.Path).RegistryPackages);
    }

    private static TheiaFileProvider Provider() => new(Path.GetTempPath(), SearchOption.TopDirectoryOnly,
        new VersionContainer(EGame.GAME_ArcRaiders), StringComparer.OrdinalIgnoreCase);

    private static Dictionary<string, GameFile> Files(params GameFile[] files) =>
        files.ToDictionary(file => file.Path, StringComparer.OrdinalIgnoreCase);

    private static RegisteredObject Registered(string package) => new(package + ".Object", package, "DataAsset",
        new Dictionary<string, string>());

    private sealed class FixtureFile(string path) : GameFile(path, 0)
    {
        public override bool IsEncrypted => false;
        public override CompressionMethod CompressionMethod => CompressionMethod.None;
        public override byte[] Read(FByteBulkDataHeader? header = null) => throw new NotSupportedException();
        public override FArchive CreateReader(FByteBulkDataHeader? header = null) => throw new NotSupportedException();
    }
}
