using AssetIndex.Discovery;
using CUE4Parse.FileProvider.Objects;
using CUE4Parse.UE4.Assets;
using CUE4Parse.UE4.IO;
using CUE4Parse.UE4.IO.Objects;
using CUE4Parse.UE4.Objects.UObject;
using CUE4Parse.UE4.Versions;

namespace AssetIndex.Tests;

public sealed class UnavailableImportTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ExactDirectStoreBindingProvesAbsenceWithoutLoadingImportedPackages(bool optional)
    {
        using var fixture = new ImportFixture(optional);

        var missing = fixture.Provider.InputIndex.MissingHard(new(fixture.Owner, -1));

        Assert.NotNull(missing);
        Assert.Equal(fixture.Target, missing.PackageId);
        var import = Assert.Single(fixture.Provider.InputIndex.Record.Imports);
        Assert.Equal(missing.OwnerFile, import.File);
        Assert.Equal(["8000000000000000"], import.ImportMap);
        Assert.Equal(["000000000000002a"], import.ExportHashes);
        Assert.False(fixture.Owner.ImportedPackages.IsValueCreated);
        Assert.False(fixture.Owner.ImportedPackagesAllVersions.IsValueCreated);
    }

    [Fact]
    public void AnOptionalStoreEntryPreventsAbsentClassification()
    {
        using var fixture = new ImportFixture(false);
        fixture.Reader.ContainerHeader!.OptionalSegmentPackageIds = [FPackageId.FromName("/Game/Absent")];
        fixture.Reader.ContainerHeader.OptionalSegmentStoreEntries = [ImportPackageIdentityTests.Store()];

        Assert.Null(fixture.Provider.InputIndex.MissingHard(new(fixture.Owner, -1)));
        Assert.Empty(fixture.Provider.InputIndex.Record.Imports);
    }

    [Fact]
    public void InvalidPackageSentinelIsNotAnUnavailableInput()
    {
        using var fixture = new ImportFixture(false);
        fixture.Reader.ContainerHeader!.StoreEntries = [ImportPackageIdentityTests.Store(new FPackageId(ulong.MaxValue))];

        Assert.Null(fixture.Provider.InputIndex.MissingHard(new(fixture.Owner, -1)));
    }

    [Theory]
    [InlineData(-2, 0UL)]
    [InlineData(-1, 1UL)]
    [InlineData(-1, 0x100000000UL)]
    public void InvalidImportSlotsOrHashBoundsDoNotProveAbsence(int packageIndex, ulong importOffset)
    {
        using var fixture = new ImportFixture(false, importOffset);

        Assert.Null(fixture.Provider.InputIndex.MissingHard(new(fixture.Owner, packageIndex)));
        Assert.Empty(fixture.Provider.InputIndex.Record.Imports);
    }

    private sealed class ImportFixture : IDisposable
    {
        public OwnerProvider Provider { get; }
        public IoPackage Owner { get; }
        public IoStoreReader Reader { get; }
        public string Target { get; } = PackageInputIndex.Id(FPackageId.FromName("/Game/Absent").id);

        public ImportFixture(bool optional, ulong importOffset = 0)
        {
            Owner = IoMetadataFixture.Create([], [], [], () => throw new InvalidOperationException("No payload expected."),
                [new FPackageObjectIndex(0x8000000000000000 | importOffset)]);
            Owner.Name = "/Game/Owner";
            typeof(IoPackage).GetField(nameof(IoPackage.ImportedPublicExportHashes))!.SetValue(Owner, new ulong[] { 42 });
            typeof(IoPackage).GetField(nameof(IoPackage.ImportedPackages))!.SetValue(Owner,
                new Lazy<IoPackage?[]>(() => throw new InvalidOperationException("Absent input must not resolve.")));
            typeof(IoPackage).GetField(nameof(IoPackage.ImportedPackagesAllVersions))!.SetValue(Owner,
                new Lazy<IPackage?[][]>(() => throw new InvalidOperationException("Absent input must not resolve alternatives.")));
            Provider = new(Owner);
            var path = Path.Combine(AppContext.BaseDirectory, "Fixtures", "IoStore", "CUE4ParseFixtures-Minimal-Zlib-Windows.utoc");
            Provider.AddFixture(path);
            Provider.Mount();
            Reader = Assert.IsType<IoStoreReader>(Assert.Single(Provider.MountedVfs));
            var header = Reader.ContainerHeader!;
            header.PackageIds = optional ? [] : [FPackageId.FromName(Owner.Name)];
            header.StoreEntries = optional ? [] : [ImportPackageIdentityTests.Store(FPackageId.FromName("/Game/Absent"))];
            header.OptionalSegmentPackageIds = optional ? [FPackageId.FromName(Owner.Name)] : [];
            header.OptionalSegmentStoreEntries = optional ? [ImportPackageIdentityTests.Store(FPackageId.FromName("/Game/Absent"))] : [];
            Provider.LoadPackage(Provider.Files.Values.First(file => file.IsUePackage));
        }

        public void Dispose() => Provider.Dispose();
    }

    private sealed class OwnerProvider(IoPackage owner) : PackageProvider(Path.GetTempPath())
    {
        public void AddFixture(string path) => PostLoadReader(new IoStoreReader(path, versions: new VersionContainer(EGame.GAME_UE5_8)));
        protected override IPackage ReadPackage(GameFile file) => owner;
    }
}
