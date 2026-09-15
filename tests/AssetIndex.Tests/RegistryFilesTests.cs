using AssetIndex.Discovery;
using CUE4Parse.Compression;
using CUE4Parse.FileProvider.Objects;
using CUE4Parse.GameTypes.Theia.FileProvider;
using CUE4Parse.UE4.Assets.Objects;
using CUE4Parse.UE4.Readers;
using CUE4Parse.UE4.Versions;

namespace AssetIndex.Tests;

public sealed class RegistryFilesTests
{
    [Theory]
    [InlineData("PioneerGame/AssetRegistry.bin")]
    [InlineData("pioneergame/assetregistry.bin")]
    public void ReadsOnlyTheWinningVersionOfEachRegistryPath(string currentPath)
    {
        using var provider = new TheiaFileProvider(Path.GetTempPath(), SearchOption.TopDirectoryOnly,
            new VersionContainer(EGame.GAME_ArcRaiders), StringComparer.OrdinalIgnoreCase);
        var previous = new ObservedFile("PioneerGame/AssetRegistry.bin");
        var current = new ObservedFile(currentPath);
        var plugin = new ObservedFile("PioneerGame/Plugins/Example/AssetRegistry.bin");
        var unrelated = new ObservedFile("PioneerGame/Other.bin");
        provider.Files.AddFiles(Files(previous), readOrder: 1);
        provider.Files.AddFiles(Files(current, plugin, unrelated), readOrder: 2);
        var issues = new List<ExtractionIssue>();

        // Fail at the reader boundary so the test observes real Registry.Read
        // selection without depending on a synthetic registry binary format.
        Assert.Throws<InvalidDataException>(() => Registry.Read(provider, issues));

        Assert.Equal(0, previous.Reads);
        Assert.Equal(1, current.Reads);
        Assert.Equal(1, plugin.Reads);
        Assert.Equal(0, unrelated.Reads);
        Assert.Equal(new[] { current.Path, plugin.Path }.Order(StringComparer.Ordinal),
            issues.Select(issue => issue.Path).Order(StringComparer.Ordinal));
        Assert.All(issues, issue => Assert.Equal("registry", issue.Stage));
    }

    private static Dictionary<string, GameFile> Files(params GameFile[] files) =>
        files.ToDictionary(file => file.Path, StringComparer.OrdinalIgnoreCase);

    private sealed class ObservedFile(string path) : GameFile(path, 0)
    {
        public int Reads { get; private set; }
        public override bool IsEncrypted => false;
        public override CompressionMethod CompressionMethod => CompressionMethod.None;
        public override byte[] Read(FByteBulkDataHeader? header = null) => throw new NotSupportedException();
        public override FArchive CreateReader(FByteBulkDataHeader? header = null)
        {
            Reads++;
            throw new InvalidDataException("Intentional registry reader failure.");
        }
    }
}
