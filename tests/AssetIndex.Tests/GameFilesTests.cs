using CUE4Parse.FileProvider.Objects;
using CUE4Parse.GameTypes.Theia.FileProvider;
using CUE4Parse.UE4.IO.Objects;
using CUE4Parse.UE4.Versions;

namespace AssetIndex.Tests;

public sealed class GameFilesTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ResolvesUnmappedVirtualRootsWithoutOverridingNormalMounts(bool normalPathExists)
    {
        var directory = Path.Combine(Path.GetTempPath(), "asset-index-package-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var source = new FileInfo(Path.Combine(directory, "WorldItemList.uasset"));
            File.WriteAllBytes(source.FullName, [1]);
            var versions = new VersionContainer(EGame.GAME_ArcRaiders);
            var physical = new OsGameFile(new DirectoryInfo(directory), source, "PioneerGame/Intermediate/CookedOnly/", versions);
            var normal = new OsGameFile(new DirectoryInfo(directory), source, "CookedOnly/", versions);
            const string canonical = "/CookedOnly/WorldItemList";
            using var provider = new TheiaFileProvider(directory, SearchOption.AllDirectories, versions, StringComparer.OrdinalIgnoreCase);
            var files = new Dictionary<string, GameFile> { [physical.Path] = physical };
            if (normalPathExists) files[normal.Path] = normal;
            provider.Files.AddFiles(files, packageFiles: new Dictionary<FPackageId, GameFile>
            {
                [FPackageId.FromName(canonical)] = physical
            });

            Assert.Equal(normalPathExists, provider.TryGetGameFile(canonical, out _));
            Assert.Equal(normalPathExists ? normal.Path : physical.Path, GameFiles.ResolvePackagePath(provider, canonical));
            Assert.Equal("/CookedOnly/Absent", GameFiles.ResolvePackagePath(provider, "/CookedOnly/Absent"));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void OpeningGameFilesResolvesPluginPackageRoots()
    {
        var directory = Path.Combine(Path.GetTempPath(), "asset-index-plugin-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(Path.Combine(directory, "Config"));
            Directory.CreateDirectory(Path.Combine(directory, "Plugins", "TestPlugin", "Content"));
            File.WriteAllText(Path.Combine(directory, "PioneerGame.uproject"), "{}");
            File.WriteAllText(Path.Combine(directory, "Config", "DefaultGame.ini"),
                "[/Script/UnrealEd.ProjectPackagingSettings]\n+CulturesToStage=en\n");
            File.WriteAllText(Path.Combine(directory, "Plugins", "TestPlugin", "TestPlugin.uplugin"),
                "{\"CanContainContent\":true}");
            File.WriteAllBytes(Path.Combine(directory, "Plugins", "TestPlugin", "Content", "Fixture.uasset"), [1, 2, 3]);
            var options = new Options(directory,
                Path.Combine(AppContext.BaseDirectory, "mappings", "ArcRaiders.usmap"), Path.Combine(directory, "output"));

            using var provider = GameFiles.Open(options);

            Assert.True(provider.SkipReferencedTextures);
            Assert.Equal("PioneerGame/Plugins/TestPlugin/Content/Fixture.uasset", provider.FixPath("/TestPlugin/Fixture"));
            Assert.True(provider.TryGetGameFile("/TestPlugin/Fixture", out _));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}
