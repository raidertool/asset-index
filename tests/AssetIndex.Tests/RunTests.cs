using System.Text.Json;

namespace AssetIndex.Tests;

public sealed class RunTests : IDisposable
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), "asset-index-test-" + Guid.NewGuid().ToString("N"));

    public RunTests() => Directory.CreateDirectory(directory);

    [Fact]
    public void ExistingOutputIsRejectedBeforeItCanBeOverwritten()
    {
        var output = Path.Combine(directory, "published");
        Directory.CreateDirectory(output);
        var snapshot = Path.Combine(output, "assets.json");
        File.WriteAllText(snapshot, "published snapshot");
        Assert.Equal(2, Program.Main(["--game-dir", directory, "--output", output]));
        Assert.Equal("published snapshot", File.ReadAllText(snapshot));
    }

    [Fact]
    public void FailedInputProducesDiagnosticsAndCanBeRetriedWithoutChangingPublishedFiles()
    {
        var published = Path.Combine(directory, "assets.json");
        File.WriteAllText(published, "published snapshot");
        foreach (var run in new[] { "first", "retry" })
        {
            var output = Path.Combine(directory, run);
            Assert.Equal(1, Program.Main(["--game-dir", directory, "--output", output]));
            using var report = JsonDocument.Parse(File.ReadAllText(Path.Combine(output, "coverage.json")));
            Assert.Equal("incomplete", report.RootElement.GetProperty("status").GetString());
            Assert.Contains(report.RootElement.GetProperty("issues").EnumerateArray(),
                issue => issue.GetProperty("message").GetString() == "No game files were mounted.");
        }
        Assert.Equal("published snapshot", File.ReadAllText(published));
    }

    [Fact]
    public void JsonPreservesSignedInt64IdsAndEscapedText()
    {
        var text = new LocalizedText(long.MinValue, "en", "Name\n\"quoted\"", "Description");
        Snapshot.Write(directory, "sample.json", text);
        var result = JsonSerializer.Deserialize<LocalizedText>(File.ReadAllText(Path.Combine(directory, "sample.json")), Snapshot.Json);
        Assert.Equal(text, result);
    }

    [Theory]
    [InlineData("--outpt", "folder")]
    [InlineData("--game-dir")]
    [InlineData("--game-dir", "game", "--game-dir", "again")]
    public void RejectsAmbiguousArguments(params string[] args) => Assert.Throws<ArgumentException>(() => Options.Parse(args));

    public void Dispose() => Directory.Delete(directory, recursive: true);
}
