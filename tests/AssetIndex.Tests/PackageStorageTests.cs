using System.Runtime.CompilerServices;
using CUE4Parse.Compression;
using CUE4Parse.FileProvider.Objects;
using CUE4Parse.FileProvider.Vfs;
using CUE4Parse.UE4.Assets.Objects;
using CUE4Parse.UE4.IO.Objects;
using CUE4Parse.UE4.Readers;

namespace AssetIndex.Tests;

public sealed class PackageStorageTests
{
    [Fact]
    public void CountsShadowedVersionsOncePerFileIdentityAndExcludesNonspooledInputs()
    {
        var old = Entry("Same.uasset", 10);
        var current = Entry("Same.uasset", 20);
        var bulk = Entry("Same.ubulk", 100);
        var files = new FileProviderDictionary();
        // These are already mounted directory entries; no payload/index parsing is needed.
        files.AddFiles(new Dictionary<string, GameFile> { [old.Path] = old }, 1, new Dictionary<FPackageId, GameFile>());
        files.AddFiles(new Dictionary<string, GameFile>
        {
            [current.Path] = current,
            ["Alias.uasset"] = current,
            [bulk.Path] = bulk,
            ["Loose.uasset"] = new NonIoFile()
        }, 2, new Dictionary<FPackageId, GameFile>());

        var actual = PackageStorage.Read(files.Values);

        Assert.Same(current, files["Same.uasset"]);
        Assert.Equal(new PackageStorage(2, 30), actual);
        Assert.Equal(new PackageStorage(0, 0), PackageStorage.Read([]));
    }

    private static FIoStoreEntry Entry(string path, long size)
    {
        var file = (FIoStoreEntry)RuntimeHelpers.GetUninitializedObject(typeof(FIoStoreEntry));
        typeof(GameFile).GetProperty(nameof(GameFile.Path))!.SetValue(file, path);
        typeof(GameFile).GetProperty(nameof(GameFile.Size))!.SetValue(file, size);
        return file;
    }

    private sealed class NonIoFile() : GameFile("Loose.uasset", 200)
    {
        public override bool IsEncrypted => false;
        public override CompressionMethod CompressionMethod => CompressionMethod.None;
        public override byte[] Read(FByteBulkDataHeader? header = null) => throw new NotSupportedException();
        public override FArchive CreateReader(FByteBulkDataHeader? header = null) => throw new NotSupportedException();
    }
}
