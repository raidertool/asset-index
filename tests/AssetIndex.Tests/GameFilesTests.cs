namespace AssetIndex.Tests;

public sealed class GameFilesTests
{
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

            Assert.Equal("PioneerGame/Plugins/TestPlugin/Content/Fixture.uasset", provider.FixPath("/TestPlugin/Fixture"));
            Assert.True(provider.TryGetGameFile("/TestPlugin/Fixture", out _));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}
