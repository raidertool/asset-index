using System.Text.Json;
using System.Text.Json.Nodes;

namespace PublishSnapshot.Tests;

public sealed partial class PublisherTests
{
    [Fact]
    public void PublicationRetainsEveryCatalogImageAndLocaleWithoutChangingTheFullPreview()
    {
        var second = AddUnownedImage("/Game/T_Second.T_Second", "Texture2D");
        AddCatalogImage("SecondaryIcon", "/Game/T_Second.T_Second", second);
        AddCatalogImage("AlternateIcon", Texture, Image);
        var unowned = AddUnownedImage("/Game/T_Unowned.T_Unowned", "Texture2D");
        WriteLines(preview, "localization/ko.jsonl.gz", new[] { new { @namespace = "Shared", key = "KOREAN", value = "한국어" } });
        ChangeJson("assets.json", rows =>
        {
            var korean = rows[0]!["text"]![0]!.DeepClone();
            korean["locale"] = "ko";
            rows[0]!["text"]!.AsArray().Add(korean);
        });
        var input = HashFiles(preview);

        var result = Publisher.Publish(preview, remote, NextExtractor, "456");

        Assert.True(result.Changed);
        Assert.Equal(DataSnapshot.Required.Concat([Image, second, "localization/ko.jsonl.gz", "metadata.json"]).Order(),
            remoteGit.Run("ls-tree", "-r", "--name-only", result.Commit).Split('\n', StringSplitOptions.RemoveEmptyEntries).Order());
        using var resources = JsonDocument.Parse(remoteGit.Run("show", result.Commit + ":resources.json"));
        Assert.Equal(new[] { "/Game/T_Second.T_Second", Texture },
            resources.RootElement.EnumerateArray().Select(resource => resource.GetProperty("path").GetString()));
        Assert.Equal(input, HashFiles(preview));
        Assert.Equal(File.ReadAllText(Path.Combine(preview, "assets.json")), remoteGit.Run("show", result.Commit + ":assets.json"));
        Assert.True(File.Exists(Path.Combine(preview, unowned)));
        Assert.True(File.Exists(Path.Combine(preview, "discovery/objects.jsonl.gz")));
        Assert.Equal(File.ReadAllText(Path.Combine(preview, "coverage.json")), remoteGit.Run("show", result.Commit + ":coverage.json"));
    }

    [Fact]
    public void ResourceOrderingDoesNotCreateAnotherSnapshot()
    {
        var second = AddUnownedImage("/Game/T_Second.T_Second", "Texture2D");
        AddCatalogImage("SecondaryIcon", "/Game/T_Second.T_Second", second);
        var first = Publisher.Publish(preview, remote, NextExtractor, "456");
        ChangeJson("resources.json", rows =>
        {
            var reversed = rows.AsArray().Select(row => row!.DeepClone()).Reverse().ToArray();
            rows.AsArray().Clear();
            foreach (var row in reversed) rows.AsArray().Add(row);
        });

        var retry = Publisher.Publish(preview, remote, InitialExtractor, "789");

        Assert.False(retry.Changed);
        Assert.Equal(first.Commit, retry.Commit);
        Assert.Equal(first.Tag, retry.Tag);
    }

    [Fact]
    public void InvalidExcludedEvidenceRejectsBeforeGitAndLeavesInputUntouched()
    {
        ChangeLines("discovery/objects.jsonl.gz", rows =>
            rows[0]!["issues"]!.AsArray().Add(new JsonObject { ["message"] = "raw evidence failed" }));
        var input = HashFiles(preview);
        var before = remoteGit.Run("show-ref");

        var error = Assert.Throws<InvalidDataException>(() => Publisher.Publish(preview,
            Path.Combine(root, "nonexistent-remote"), NextExtractor, "456"));

        Assert.Contains("Object evidence contains diagnostics", error.Message);
        Assert.Equal(before, remoteGit.Run("show-ref"));
        Assert.Equal(input, HashFiles(preview));
    }

    [Fact]
    public void APreviewWithNoCatalogImagesPublishesAnEmptyResourceList()
    {
        ChangeJson("assets.json", rows => rows[0]!["images"] = new JsonArray());
        ChangeJson("coverage.json", report => report["images"] = 0);
        using var captured = Preview.Read(preview);
        using var snapshot = DataSnapshot.Create(captured);

        Assert.DoesNotContain(Image, snapshot.Files.Keys);
        using var resources = Preview.ReadJson(snapshot.Files, "resources.json");
        Assert.Empty(resources.RootElement.EnumerateArray());
        Assert.True(File.Exists(captured.Files[Image].Path));
    }

    [Theory]
    [InlineData("assets.json")]
    [InlineData("coverage.json")]
    [InlineData("localization/en.jsonl.gz")]
    [InlineData("image")]
    public void EveryPublishedBlobHasTheSameSizeLimit(string selected)
    {
        using var captured = Preview.Read(preview);
        var path = selected == "image" ? Image : selected;
        // Exercise the byte-count boundary without allocating a 100 MiB fixture.
        captured.Snapshot.Files[path] = captured.Files[path] with { Length = DataSnapshot.MaximumBlobBytes };
        using var accepted = DataSnapshot.Create(captured);
        captured.Snapshot.Files[path] = captured.Files[path] with { Length = DataSnapshot.MaximumBlobBytes + 1 };

        var error = Assert.Throws<InvalidDataException>(() => DataSnapshot.Create(captured));

        Assert.Equal("Published file exceeds 100 MiB: " + path, error.Message);
    }

    [Fact]
    public void ExcludedStreamsHaveNoGitBlobLimitAndProjectionDisposesOnlyItsOwnFile()
    {
        using var captured = Preview.Read(preview);
        const string evidence = "discovery/objects.jsonl.gz";
        captured.Snapshot.Files[evidence] = captured.Files[evidence] with { Length = DataSnapshot.MaximumBlobBytes + 1 };
        var snapshot = DataSnapshot.Create(captured);
        var filtered = snapshot.Files["resources.json"].Path;
        Assert.NotEqual(captured.Files["resources.json"].Path, filtered);
        Assert.Equal(captured.Files[Image].Path, snapshot.Files[Image].Path);

        snapshot.Dispose();

        Assert.False(File.Exists(filtered));
        Assert.True(File.Exists(captured.Files["resources.json"].Path));
        Assert.True(File.Exists(captured.Files[evidence].Path));
        Assert.True(File.Exists(captured.Files[Image].Path));
    }

    private void AddCatalogImage(string field, string resource, string file) => ChangeJson("assets.json", rows =>
        rows[0]!["images"]!.AsArray().Add(new JsonObject
        {
            ["field"] = field,
            ["source"] = "/Game/DA_Test.DA_Test",
            ["resource"] = resource,
            ["status"] = "exported",
            ["file"] = file,
            ["width"] = 2,
            ["height"] = 1
        }));
}
