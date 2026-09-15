using System.Text.Json;
using System.IO.Compression;

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
    public void WriteFailureLeavesNoPartialSnapshotAndAllowsTheSameInputToBeRetried()
    {
        Assert.Throws<IOException>(() => Snapshot.WriteFile(directory, "atomic.json", stream =>
        {
            stream.Write("partial"u8);
            throw new IOException("Deliberate write failure.");
        }));
        Assert.Empty(Directory.EnumerateFiles(directory));
        Snapshot.Write(directory, "atomic.json", new { value = "complete" });
        var original = File.ReadAllBytes(Path.Combine(directory, "atomic.json"));
        Assert.Throws<IOException>(() => Snapshot.Write(directory, "atomic.json", new { value = "replacement" }));
        Assert.Equal(original, File.ReadAllBytes(Path.Combine(directory, "atomic.json")));
        Assert.Single(Directory.EnumerateFiles(directory));
    }

    [Theory]
    [InlineData("../escaped.json")]
    [InlineData("localization/../../escaped.json")]
    [InlineData("/tmp/escaped.json")]
    [InlineData("localization\\..\\escaped.json")]
    public void GameDerivedFilenamesCannotEscapeTheOutput(string name)
    {
        Assert.Throws<InvalidDataException>(() => Snapshot.Write(directory, name, "invalid"));
        Assert.Throws<InvalidDataException>(() => new JsonLinesFile<int>(directory, name));
        Assert.Empty(Directory.EnumerateFileSystemEntries(directory));
    }

    [Fact]
    public void JsonLinesFilesRequireCompletionAndNeverReplacePublishedData()
    {
        const string name = "discovery/rows.jsonl.gz";
        var path = Path.Combine(directory, name);
        using (var partial = new JsonLinesFile<string>(directory, name))
        {
            partial.Write("partial");
            Assert.False(File.Exists(path));
        }
        Assert.Empty(Directory.EnumerateFiles(Path.GetDirectoryName(path)!));

        using (var complete = new JsonLinesFile<string>(directory, name))
        {
            complete.Write("first\nline");
            complete.Write("한국어");
            complete.Complete();
        }
        var published = File.ReadAllBytes(path);
        using (var gzip = new GZipStream(File.OpenRead(path), CompressionMode.Decompress))
        using (var reader = new StreamReader(gzip))
        {
            Assert.Equal("first\nline", JsonSerializer.Deserialize<string>(reader.ReadLine()!));
            Assert.Equal("한국어", JsonSerializer.Deserialize<string>(reader.ReadLine()!));
            Assert.Null(reader.ReadLine());
        }
        using (var replacement = new JsonLinesFile<string>(directory, name))
        {
            replacement.Write("replacement");
            Assert.Throws<IOException>(replacement.Complete);
        }
        Assert.Equal(published, File.ReadAllBytes(path));
        Assert.Single(Directory.EnumerateFiles(Path.GetDirectoryName(path)!));
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

    [Theory]
    [InlineData(long.MinValue)]
    [InlineData(long.MaxValue)]
    [InlineData(9007199254740993L)]
    [InlineData(-9007199254740993L)]
    public void JsonWritesIdsAsExactDecimalStringsForBrowserConsumers(long id)
    {
        var asset = new AssetRecord(id, [], [], [], []);
        Snapshot.Write(directory, "sample.json", asset);
        using var document = JsonDocument.Parse(File.ReadAllText(Path.Combine(directory, "sample.json")));

        var value = document.RootElement.GetProperty("id");
        Assert.Equal(JsonValueKind.String, value.ValueKind);
        Assert.Equal(id.ToString(System.Globalization.CultureInfo.InvariantCulture), value.GetString());
    }

    [Fact]
    public void JsonPreservesAllObjectReferencesAndNestedTextWithoutRepeatedIds()
    {
        var name = "Name\n\"quoted\"";
        var asset = new AssetRecord(42,
            [new("DA_Item", "Item", "/Game/DA_Item"), new("DA_Persistence", "Persistence", "/Game/DA_Persistence")],
            [new("UI_Item", "UI", "/Game/UI_Item")],
            [new("en", name, "Description")], []);
        Snapshot.Write(directory, "sample.json", asset);
        using var document = JsonDocument.Parse(File.ReadAllText(Path.Combine(directory, "sample.json")));
        var row = document.RootElement;

        Assert.Equal(2, row.GetProperty("definitions").GetArrayLength());
        Assert.Equal("/Game/DA_Persistence", row.GetProperty("definitions")[1].GetProperty("path").GetString());
        Assert.Equal("/Game/UI_Item", row.GetProperty("metadata")[0].GetProperty("path").GetString());
        var text = row.GetProperty("text")[0];
        Assert.Equal(["locale", "displayName", "description"], text.EnumerateObject().Select(property => property.Name));
        Assert.Equal(name, text.GetProperty("displayName").GetString());
        Assert.False(row.TryGetProperty("name", out _));
        Assert.False(row.TryGetProperty("path", out _));
    }

    [Theory]
    [InlineData("--outpt", "folder")]
    [InlineData("--game-dir")]
    [InlineData("--game-dir", "game", "--game-dir", "again")]
    public void RejectsAmbiguousArguments(params string[] args) => Assert.Throws<ArgumentException>(() => Options.Parse(args));

    public void Dispose() => Directory.Delete(directory, recursive: true);
}
