using CUE4Parse.Compression;
using CUE4Parse.FileProvider.Objects;
using CUE4Parse.UE4.Assets;
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

    private sealed class CountingProvider(Exception? failure = null) : PackageProvider(Path.GetTempPath())
    {
        public int Reads;

        protected override IPackage ReadPackage(GameFile file)
        {
            Interlocked.Increment(ref Reads);
            if (failure is not null) throw failure;
            return new FixturePackage();
        }
    }

    private sealed class FixtureFile(string path) : GameFile(path, 0)
    {
        public override bool IsEncrypted => false;
        public override CompressionMethod CompressionMethod => CompressionMethod.None;
        public override byte[] Read(FByteBulkDataHeader? header = null) => throw new NotSupportedException();
        public override FArchive CreateReader(FByteBulkDataHeader? header = null) => throw new NotSupportedException();
    }
}
