using System.Runtime.CompilerServices;
using CUE4Parse.Compression;
using CUE4Parse.FileProvider.Objects;
using CUE4Parse.UE4.Assets;
using CUE4Parse.UE4.Assets.Exports;
using CUE4Parse.UE4.Assets.Objects;
using CUE4Parse.UE4.Readers;

namespace AssetIndex.Tests;

public sealed class PackageProviderTests
{
    [Fact]
    public async Task ConcurrentSyncAndAsyncLoadsShareOnePackage()
    {
        var file = new FixtureFile("Shared.uasset");
        using var provider = new CountingProvider();
        provider.Files.AddFiles(new Dictionary<string, GameFile> { [file.Path] = file });

        var loads = Enumerable.Range(0, 8).Select(index => index % 2 == 0
            ? provider.LoadPackageAsync(file)
            : Task.Run(() => provider.LoadPackage(file.Path)));
        var packages = await Task.WhenAll(loads);

        Assert.All(packages, package => Assert.Same(packages[0], package));
        Assert.Same(packages[0], provider.LoadPackage(file));
        Assert.Equal(1, provider.Reads);
    }

    [Fact]
    public void DifferentMountedVersionsOfOnePathStayDistinct()
    {
        var original = new FixtureFile("Shared.uasset");
        var update = new FixtureFile("Shared.uasset");
        using var provider = new CountingProvider();

        var first = provider.LoadPackage(original);
        var second = provider.LoadPackage(update);

        Assert.NotSame(first, second);
        Assert.Same(first, provider.LoadPackage(original));
        Assert.Same(second, provider.LoadPackage(update));
        Assert.Same(original, provider.SourceFile(first));
        Assert.Same(update, provider.SourceFile(second));
        Assert.Null(provider.SourceFile(new FixturePackage()));
        Assert.Equal(2, provider.Reads);
    }

    [Fact]
    public async Task ConstructionFailuresAreSharedUntilANewRun()
    {
        var file = new FixtureFile("Broken.uasset");
        var failure = new InvalidDataException("Invalid package header.");
        using var provider = new CountingProvider(failure);

        Assert.Same(failure, Assert.Throws<InvalidDataException>(() => provider.LoadPackage(file)));
        Assert.Same(failure, await Assert.ThrowsAsync<InvalidDataException>(() => provider.LoadPackageAsync(file)));
        Assert.False(provider.TryLoadPackage(file, out _));
        Assert.Equal(1, provider.Reads);

        using var retry = new CountingProvider();
        Assert.NotNull(retry.LoadPackage(file));
        Assert.Equal(1, retry.Reads);
    }

    [Fact]
    public void RecursiveConstructionFailsOnceAndRemainsCached()
    {
        var file = new FixtureFile("Recursive.uasset");
        using var provider = new RecursiveProvider();

        var failure = Assert.Throws<InvalidOperationException>(() => provider.LoadPackage(file));

        Assert.Same(failure, Assert.Throws<InvalidOperationException>(() => provider.LoadPackage(file)));
        Assert.Equal(1, provider.Reads);
    }

    [Fact]
    public void UnreferencedPackagesCanBeCollectedAndReloaded()
    {
        var file = new FixtureFile("Shared.uasset");
        using var provider = new CountingProvider();
        var previous = LoadWeak(provider, file);

        Collect(previous);
        Assert.Equal(1, provider.Reads);
        Assert.NotNull(provider.LoadPackage(file));
        Assert.Equal(2, provider.Reads);
    }

    [Fact]
    public void LiveExportKeepsItsCanonicalPackage()
    {
        var file = new FixtureFile("Shared.uasset");
        using var provider = new CountingProvider(create: CreatePackage);
        var source = provider.LoadPackage(file).ExportsLazy[1].Value;

        GC.Collect();

        Assert.Same(source.Owner, provider.LoadPackage(file));
        Assert.Same(source, provider.Load(provider.Locate(source)));
        Assert.Equal(1, provider.Reads);
    }

    [Fact]
    public void DetachedLocatorReloadsTheSamePhysicalVersionAndNestedExport()
    {
        var original = new FixtureFile("Shared.uasset");
        var replacement = new FixtureFile("Shared.uasset");
        using var provider = new CountingProvider(create: CreatePackage);
        provider.Files.AddFiles(new Dictionary<string, GameFile> { [replacement.Path] = replacement });
        var (previous, location) = LocateWeak(provider, original);
        Collect(previous);

        var source = provider.Load(location with { Path = "/game/shared.root:child" });

        Assert.Same(original, location.File);
        Assert.Equal(1, location.ExportIndex);
        Assert.Equal("/Game/Shared.Root:Child", location.Path);
        Assert.Equal("Child", source.Name);
        Assert.Same(original, provider.Locate(source).File);
        Assert.NotSame(source.Owner, provider.LoadPackage(replacement));
        Assert.Equal(3, provider.Reads);
    }

    [Fact]
    public void LocatingDoesNotDecodeSiblingExports()
    {
        var file = new FixtureFile("Shared.uasset");
        using var provider = new CountingProvider(create: CreatePackage);
        var package = (CrawlerPackage)provider.LoadPackage(file);
        var source = package.ExportsLazy[1].Value;

        Assert.Equal(1, provider.Locate(source).ExportIndex);

        Assert.Equal([1], package.BodyReads);
        Assert.False(package.ExportsLazy[0].IsValueCreated);
    }

    [Fact]
    public void UnownedAndUnindexedObjectsCannotProduceLocators()
    {
        var file = new FixtureFile("Shared.uasset");
        using var provider = new CountingProvider(create: CreatePackage);
        var package = provider.LoadPackage(file);
        var stranger = CreatePackage(file).ExportsLazy[0].Value;
        var unindexed = new UObject { Name = "Absent", Outer = new ResolvedPackageObject(package) };

        Assert.Throws<InvalidDataException>(() => provider.Locate(new UObject()));
        Assert.Throws<InvalidDataException>(() => provider.Locate(stranger));
        Assert.Throws<InvalidDataException>(() => provider.Locate(unindexed));
    }

    [Fact]
    public void RepeatedObjectIdentityIsAmbiguous()
    {
        var file = new FixtureFile("Shared.uasset");
        using var provider = new CountingProvider(create: CreatePackage);
        var package = provider.LoadPackage(file);
        var source = package.ExportsLazy[0].Value;
        package.ExportsLazy[1] = new Lazy<UObject>(() => source);
        _ = package.ExportsLazy[1].Value;

        Assert.Throws<InvalidDataException>(() => provider.Locate(source));
    }

    [Fact]
    public void APackageInstanceCannotBeReassignedToAnotherPhysicalFile()
    {
        var original = new FixtureFile("Shared.uasset");
        var replacement = new FixtureFile("Shared.uasset");
        var package = CreatePackage(original);
        using var provider = new CountingProvider(create: _ => package);
        var source = provider.LoadPackage(original).ExportsLazy[0].Value;

        Assert.Throws<InvalidDataException>(() => provider.LoadPackage(replacement));

        Assert.Same(original, provider.Locate(source).File);
        Assert.Same(package, provider.LoadPackage(original));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(2)]
    [InlineData(int.MaxValue)]
    public void InvalidLocatorIndexDoesNotDecodeAnyExport(int index)
    {
        var file = new FixtureFile("Shared.uasset");
        using var provider = new CountingProvider(create: CreatePackage);
        var package = (CrawlerPackage)provider.LoadPackage(file);

        Assert.Throws<InvalidDataException>(() => provider.Load(new(file, index, "/Game/Shared.Root")));

        Assert.Empty(package.BodyReads);
    }

    [Theory]
    [InlineData("/Game/Other.Root:Child")]
    [InlineData("/Game/Shared.Child")]
    [InlineData("/Game/Shared.Root:Sibling")]
    public void LocatorPathMustMatchTheFullOuterChain(string path)
    {
        var file = new FixtureFile("Shared.uasset");
        using var provider = new CountingProvider(create: CreatePackage);

        Assert.Throws<InvalidDataException>(() => provider.Load(new(file, 1, path)));
    }

    [Fact]
    public void FailedReconstructionIsCachedAfterCollection()
    {
        var file = new FixtureFile("Shared.uasset");
        var failure = new InvalidDataException("Changed package source.");
        var reads = 0;
        using var provider = new CountingProvider(create: input => ++reads == 1 ? CreatePackage(input) : throw failure);
        var (previous, location) = LocateWeak(provider, file);
        Collect(previous);

        Assert.Same(failure, Assert.Throws<InvalidDataException>(() => provider.Load(location)));
        Assert.Same(failure, Assert.Throws<InvalidDataException>(() => provider.Load(location)));
        Assert.Equal(2, provider.Reads);
    }

    [Fact]
    public void DisposalRejectsFurtherLoadsAndLocationLookups()
    {
        var file = new FixtureFile("Shared.uasset");
        var provider = new CountingProvider(create: CreatePackage);
        var source = provider.LoadPackage(file).ExportsLazy[0].Value;
        var location = provider.Locate(source);
        provider.Dispose();

        Assert.Throws<ObjectDisposedException>(() => provider.LoadPackage(file));
        Assert.Throws<ObjectDisposedException>(() => provider.Load(location));
        Assert.Throws<ObjectDisposedException>(() => provider.Locate(source));
        Assert.Equal(1, provider.Reads);
    }

    private static IPackage CreatePackage(GameFile file) => new CrawlerPackage(file.Path, "/Game/Shared",
        new("Root"), new("Child") { OuterIndex = 0 });

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference<IPackage> LoadWeak(PackageProvider provider, GameFile file) => new(provider.LoadPackage(file));

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static (WeakReference<IPackage>, ObjectLocation) LocateWeak(PackageProvider provider, GameFile file)
    {
        var package = provider.LoadPackage(file);
        return (new(package), provider.Locate(package.ExportsLazy[1].Value));
    }

    private static void Collect(WeakReference<IPackage> package)
    {
        for (var attempt = 0; attempt < 3; attempt++)
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
        }
        Assert.False(package.TryGetTarget(out _));
    }

    private sealed class CountingProvider(Exception? failure = null, Func<GameFile, IPackage>? create = null) : PackageProvider(Path.GetTempPath())
    {
        public int Reads;

        protected override IPackage ReadPackage(GameFile file)
        {
            Interlocked.Increment(ref Reads);
            if (failure is not null) throw failure;
            return create?.Invoke(file) ?? new FixturePackage();
        }
    }

    private sealed class FixtureFile(string path) : GameFile(path, 0)
    {
        public override bool IsEncrypted => false;
        public override CompressionMethod CompressionMethod => CompressionMethod.None;
        public override byte[] Read(FByteBulkDataHeader? header = null) => throw new NotSupportedException();
        public override FArchive CreateReader(FByteBulkDataHeader? header = null) => throw new NotSupportedException();
    }

    private sealed class RecursiveProvider() : PackageProvider(Path.GetTempPath())
    {
        public int Reads;
        protected override IPackage ReadPackage(GameFile file)
        {
            Reads++;
            return LoadPackage(file);
        }
    }
}
