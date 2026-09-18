using System.Text.Json;
using System.Text.Json.Nodes;

namespace PublishSnapshot.Tests;

public sealed partial class PublisherTests
{
    [Fact]
    public void PublicationRetainsEveryImageAndLocaleWithoutChangingTheFullPreview()
    {
        var second = AddUnownedImage("/Game/T_Second.T_Second", "Texture2D");
        AddCatalogImage("BigIcon", "/Game/T_Second.T_Second", second);
        AddCatalogImage("TinyIcon", Texture, Image);
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
        Assert.Equal(DataSnapshot.Required.Concat([Image, second, unowned, "localization/ko.jsonl.gz", "metadata.json"]).Order(),
            remoteGit.Run("ls-tree", "-r", "--name-only", result.Commit).Split('\n', StringSplitOptions.RemoveEmptyEntries).Order());
        using var resources = JsonDocument.Parse(remoteGit.Run("show", result.Commit + ":resources.json"));
        Assert.Equal(new[] { "/Game/T_Second.T_Second", Texture, "/Game/T_Unowned.T_Unowned" },
            resources.RootElement.EnumerateArray().Select(resource => resource.GetProperty("path").GetString()));
        Assert.Equal(input, HashFiles(preview));
        Assert.Equal(File.ReadAllText(Path.Combine(preview, "assets.json")), remoteGit.Run("show", result.Commit + ":assets.json"));
        Assert.True(File.Exists(Path.Combine(preview, unowned)));
        Assert.True(File.Exists(Path.Combine(preview, "discovery/objects.jsonl.gz")));
        using var coverage = JsonDocument.Parse(remoteGit.Run("show", result.Commit + ":coverage.json"));
        Assert.Equal(2, coverage.RootElement.GetProperty("formatVersion").GetInt32());
        Assert.Equal(0, coverage.RootElement.GetProperty("issueCounts").GetProperty("total").GetInt32());
        Assert.False(coverage.RootElement.TryGetProperty("issues", out _));
        Assert.False(coverage.RootElement.TryGetProperty("notices", out _));
        Assert.False(coverage.RootElement.GetProperty("discovery").TryGetProperty("nativeScope", out _));
    }

    [Fact]
    public void ResourceOrderingDoesNotCreateAnotherSnapshot()
    {
        var second = AddUnownedImage("/Game/T_Second.T_Second", "Texture2D");
        AddCatalogImage("BigIcon", "/Game/T_Second.T_Second", second);
        var first = Publisher.Publish(preview, remote, NextExtractor, "456");
        ChangeJson("resources.json", rows =>
        {
            var reversed = rows.AsArray().Select(row => row!.DeepClone()).Reverse().ToArray();
            rows.AsArray().Clear();
            foreach (var row in reversed) rows.AsArray().Add(row);
        });

        var retry = Publisher.Publish(preview, remote, InitialExtractor, "456");

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
    public void APreviewWithNoCatalogImagesStillPublishesItsResources()
    {
        ChangeLines("discovery/objects.jsonl.gz", rows =>
        {
            var source = rows[0]!;
            source["properties"]!.AsArray().RemoveAt(3);
            var references = source["references"]!.AsArray();
            references.Remove(references.Single(reference => reference!["pointer"]!.GetValue<string>() == "/Properties/3"));
        });
        ChangeJson("assets.json", rows => rows[0]!["images"] = new JsonArray());
        ChangeJson("coverage.json", report => report["images"] = 0);
        using var captured = Preview.Read(preview);
        using var snapshot = DataSnapshot.Create(captured);

        Assert.Contains(Image, snapshot.Files.Keys);
        using var resources = Preview.ReadJson(snapshot.Files, "resources.json");
        Assert.Equal(Texture, Assert.Single(resources.RootElement.EnumerateArray()).GetProperty("path").GetString());
        Assert.True(File.Exists(captured.Files[Image].Path));
    }

    [Fact]
    public void KeepingThePngDoesNotPermitDroppingItsDecodedCatalogAssociation()
    {
        ChangeJson("assets.json", rows => rows[0]!["images"] = new JsonArray());
        ChangeJson("coverage.json", report => report["images"] = 0);

        var error = Assert.Throws<InvalidDataException>(() => Preview.Read(preview));

        Assert.Contains("omits decoded image association", error.Message);
        Assert.True(File.Exists(Path.Combine(preview, Image)));
    }

    [Theory]
    [InlineData("assets.json")]
    [InlineData("asset_index.csv")]
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

    private void AddCatalogImage(string field, string resource, string file)
    {
        ChangeJson("assets.json", rows => rows[0]!["images"]!.AsArray().Add(new JsonObject
        {
            ["field"] = field,
            ["source"] = "/Game/DA_Test.DA_Test",
            ["resource"] = resource,
            ["status"] = "exported",
            ["file"] = file,
            ["width"] = 2,
            ["height"] = 1
        }));
        ChangeLines("discovery/objects.jsonl.gz", rows =>
        {
            var source = rows[0]!;
            var pointer = "/Properties/" + source["properties"]!.AsArray().Count;
            source["properties"]!.AsArray().Add(PropertyHeader(pointer, field, "SoftObjectProperty"));
            var reference = source["references"]![0]!.DeepClone();
            reference["pointer"] = pointer;
            reference["targetPath"] = resource;
            source["references"]!.AsArray().Add(reference);
        });
    }
}
