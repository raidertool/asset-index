using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;

namespace PublishSnapshot.Tests;

public sealed partial class PublisherTests
{
    [Theory]
    [InlineData("resources.json")]
    [InlineData("discovery/objects.jsonl.gz")]
    [InlineData("discovery/files.jsonl.gz")]
    [InlineData("discovery/registry.jsonl.gz")]
    [InlineData("discovery/packages.jsonl.gz")]
    [InlineData("localization/en.jsonl.gz")]
    public void MissingResourceEvidenceCannotChangePublishedReferences(string path)
    {
        File.Delete(Path.Combine(preview, path));
        AssertRejected();
    }

    [Theory]
    [InlineData("truncated-gzip")]
    [InlineData("corrupt-gzip")]
    [InlineData("resource-failed")]
    [InlineData("resource-duplicate")]
    [InlineData("missing-object")]
    [InlineData("object-issues")]
    [InlineData("package-partial")]
    [InlineData("object-count")]
    [InlineData("export-object-count")]
    [InlineData("resource-count")]
    [InlineData("old-image-field")]
    [InlineData("old-resource-count")]
    [InlineData("selected-reference")]
    [InlineData("template-provenance")]
    [InlineData("missing-selected-name")]
    [InlineData("duplicate-localization")]
    public void InvalidResourceEvidenceCannotChangePublishedReferences(string mutation)
    {
        switch (mutation)
        {
            case "truncated-gzip":
                var path = Path.Combine(preview, "discovery/objects.jsonl.gz");
                var bytes = File.ReadAllBytes(path);
                File.WriteAllBytes(path, bytes[..^4]);
                break;
            case "corrupt-gzip": File.WriteAllText(Path.Combine(preview, "localization/en.jsonl.gz"), "invalid gzip"); break;
            case "resource-failed": ChangeJson("resources.json", n => n[0]!["status"] = "failed"); break;
            case "resource-duplicate": ChangeJson("resources.json", n => n.AsArray().Add(n[0]!.DeepClone())); break;
            case "missing-object": ChangeJson("resources.json", n => n[0]!["path"] = "/Game/Undiscovered.Undiscovered"); break;
            case "object-issues": ChangeLines("discovery/objects.jsonl.gz", rows => rows[0]!["issues"]!.AsArray().Add(new JsonObject { ["message"] = "failed" })); break;
            case "package-partial": ChangeLines("discovery/packages.jsonl.gz", rows => rows[0]!["status"] = "partial"); break;
            case "export-object-count": ChangeLines("discovery/packages.jsonl.gz", rows => { rows[0]!["exports"] = 2; rows[0]!["loaded"] = 2; }); break;
            case "object-count": ChangeJson("coverage.json", n => n["discovery"]!["objects"] = 3); break;
            case "resource-count": ChangeJson("coverage.json", n => n["discovery"]!["resources"] = 2); break;
            case "old-image-field":
                ChangeJson("assets.json", n =>
            {
                var image = n[0]!["images"]![0]!.AsObject();
                image["texture"] = image["resource"]!.DeepClone();
                image.Remove("resource");
            }); break;
            case "old-resource-count":
                ChangeJson("coverage.json", n =>
            {
                var discovery = n["discovery"]!.AsObject();
                discovery["textures"] = discovery["resources"]!.DeepClone();
                discovery.Remove("resources");
            }); break;
            case "selected-reference": ChangeJson("assets.json", n => n[0]!["presentation"]!["name"]!["source"] = "Unbacked label"); break;
            case "template-provenance": ChangeJson("assets.json", n => n[0]!["presentation"]!["candidates"]![0]!["definedAt"] = "/Game/Missing.Missing"); break;
            case "missing-selected-name": ChangeJson("assets.json", n => n[0]!["presentation"]!["name"] = null); break;
            case "duplicate-localization": ChangeLines("localization/en.jsonl.gz", rows => rows.Add(rows[0]!.DeepClone())); break;
            default: throw new ArgumentOutOfRangeException(nameof(mutation));
        }
        AssertRejected();
    }

    [Theory]
    [InlineData("localization")]
    [InlineData("object")]
    [InlineData("registry")]
    public void EvidenceChangesCreateAVersionEvenWhenCatalogBytesAreIdentical(string kind)
    {
        WritePreview(preview, "Initial name");
        var original = File.ReadAllBytes(Path.Combine(preview, "assets.json"));
        switch (kind)
        {
            case "localization": ChangeLines("localization/en.jsonl.gz", rows => rows[0]!["value"] = "Changed unowned text"); break;
            case "object":
                ChangeLines("discovery/objects.jsonl.gz", rows => rows[0]!["values"]!.AsArray().Add(new JsonObject
                { ["pointer"] = "/Properties/Ids/0", ["type"] = "Int64Property", ["kind"] = "integer", ["value"] = "-9223372036854775808" })); break;
            case "registry": ChangeLines("discovery/registry.jsonl.gz", rows => rows[0]!["tags"]!["NewTag"] = "New evidence"); break;
        }
        var result = Publisher.Publish(preview, remote, NextExtractor, "456");
        Assert.True(result.Changed);
        Assert.NotEqual(initialCommit, result.Commit);
        Assert.Equal(original, File.ReadAllBytes(Path.Combine(preview, "assets.json")));
        Assert.False(Publisher.Publish(preview, remote, NextExtractor, "456").Changed);
    }

    [Theory]
    [InlineData("/Game/T_Unowned.T_Unowned", "Texture2D")]
    [InlineData("/Game/MI_Unowned.MI_Unowned", "MaterialInstanceConstant")]
    public void AnUnownedImageIsPublishedThroughTheResourceInventory(string resource, string resourceClass)
    {
        WritePreview(preview, "Initial name");
        var file = "images/" + Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(resource))) + ".png";
        File.Copy(Path.Combine(preview, Image), Path.Combine(preview, file));
        ChangeJson("resources.json", rows => rows.AsArray().Add(new JsonObject
        {
            ["path"] = resource,
            ["status"] = "exported",
            ["file"] = file,
            ["width"] = 2,
            ["height"] = 1
        }));
        ChangeLines("discovery/objects.jsonl.gz", rows =>
        {
            var evidence = rows[1]!.DeepClone();
            evidence["path"] = resource;
            evidence["class"] = resourceClass;
            rows.Add(evidence);
        });
        AddUnindexedPackage("PioneerGame/Content/" + resource["/Game/".Length..].Split('.', 2)[0] + ".uasset", loaded: true, exports: 1);
        ChangeJson("coverage.json", n => { n["discovery"]!["objects"] = 3; n["discovery"]!["resources"] = 2; });

        var result = Publisher.Publish(preview, remote, NextExtractor, "456");

        Assert.True(result.Changed);
        Assert.Equal("blob", remoteGit.Run("cat-file", "-t", result.Commit + ":" + file).Trim());
        File.WriteAllText(Path.Combine(preview, file), "broken unowned image");
        AssertRejected();
    }

    [Fact]
    public void ValidationRetainsPrivateFilesIfTheInputChangesLater()
    {
        using var captured = Preview.Read(preview);
        var original = File.ReadAllBytes(captured.Files["assets.json"].Path);
        File.WriteAllText(Path.Combine(preview, "assets.json"), "changed after validation");
        File.Delete(Path.Combine(preview, Image));

        Assert.Equal(original, File.ReadAllBytes(captured.Files["assets.json"].Path));
        Assert.True(captured.Files[Image].Matches(captured.Files[Image].Path));
    }

    private void AssertRejected()
    {
        var before = remoteGit.Run("show-ref");
        Assert.ThrowsAny<Exception>(() => Publisher.Publish(preview, remote, NextExtractor, "456"));
        Assert.Equal(before, remoteGit.Run("show-ref"));
    }

    private void ChangeLines(string file, Action<List<JsonNode?>> change)
    {
        var rows = new List<JsonNode?>();
        using (var gzip = new GZipStream(File.OpenRead(Path.Combine(preview, file)), CompressionMode.Decompress))
        using (var reader = new StreamReader(gzip))
            while (reader.ReadLine() is { } line) rows.Add(JsonNode.Parse(line));
        change(rows);
        WriteLines(preview, file, rows);
    }
}
