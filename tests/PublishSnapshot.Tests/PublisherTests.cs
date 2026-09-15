using System.Text.Json;
using System.Text.Json.Nodes;
using System.Security.Cryptography;
using System.Text;
using System.IO.Compression;
using SkiaSharp;

namespace PublishSnapshot.Tests;

public sealed partial class PublisherTests : IDisposable
{
    private const string InitialExtractor = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
    private const string NextExtractor = "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb";
    private const string Texture = "/Game/T_Test.T_Test";
    private static readonly string Image = "images/" + Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(Texture))) + ".png";
    private readonly string root = Path.Combine(Path.GetTempPath(), "snapshot-publisher-test-" + Guid.NewGuid().ToString("N"));
    private readonly string remote;
    private readonly string preview;
    private readonly string seed;
    private readonly Git remoteGit;
    private readonly string initialCommit;

    public PublisherTests()
    {
        Directory.CreateDirectory(root);
        remote = Path.Combine(root, "remote.git");
        preview = Path.Combine(root, "preview");
        seed = Path.Combine(root, "seed");
        new Git(root).Run("init", "--bare", "--quiet", remote);
        remoteGit = new Git(remote);
        WritePreview(seed, "Initial name");
        File.WriteAllBytes(Path.Combine(seed, "metadata.json"), JsonSerializer.SerializeToUtf8Bytes(Metadata.Create(InitialExtractor, "123"), Preview.Json));
        var git = new Git(seed);
        git.Run("init", "--quiet");
        Commit(git);
        initialCommit = git.Run("rev-parse", "HEAD").Trim();
        git.Run("remote", "add", "origin", remote);
        git.Run("push", "--quiet", "--atomic", "origin", "HEAD:refs/heads/data", "HEAD:refs/heads/main", $"HEAD:refs/tags/{Publisher.Tag("123", initialCommit)}");
        WritePreview(preview, "New name");
    }

    [Fact]
    public void PublishesOnlyGeneratedFilesWithExactMetadataAndLightweightTag()
    {
        var result = Publisher.Publish(preview, remote, NextExtractor, "456");

        Assert.True(result.Changed);
        Assert.Equal(result.Commit, RemoteRef("refs/heads/data"));
        Assert.Equal(initialCommit, RemoteRef("refs/heads/main"));
        Assert.Equal("arc-456-" + result.Commit[..12], result.Tag);
        Assert.Equal(result.Commit, RemoteRef("refs/tags/" + result.Tag));
        Assert.Equal("commit", remoteGit.Run("cat-file", "-t", "refs/tags/" + result.Tag).Trim());
        Assert.Equal(SnapshotFiles.Required.Append(Image).Append("metadata.json").Order(),
            remoteGit.Run("ls-tree", "-r", "--name-only", result.Commit).Split('\n', StringSplitOptions.RemoveEmptyEntries).Order());
        using var metadata = JsonDocument.Parse(remoteGit.Run("show", result.Commit + ":metadata.json"));
        Assert.Equal(1, metadata.RootElement.GetProperty("formatVersion").GetInt32());
        Assert.Equal(NextExtractor, metadata.RootElement.GetProperty("extractorCommit").GetString());
        Assert.Equal("456", metadata.RootElement.GetProperty("steam").GetProperty("manifestId").GetString());
    }

    [Fact]
    public void IdenticalPayloadKeepsOldCommitTagAndMetadataDespiteNewProvenance()
    {
        WritePreview(preview, "Initial name");
        ChangeJson("coverage.json", node => node["discovery"]!["mappingSha256"] = new string('c', 64));
        var references = remoteGit.Run("show-ref");

        var result = Publisher.Publish(preview, remote, NextExtractor, "18446744073709551615");

        Assert.False(result.Changed);
        Assert.Equal(initialCommit, result.Commit);
        Assert.Equal(Publisher.Tag("123", initialCommit), result.Tag);
        Assert.Equal(references, remoteGit.Run("show-ref"));
        Assert.Contains(InitialExtractor, remoteGit.Run("show", "refs/heads/data:metadata.json"));
        Assert.DoesNotContain(NextExtractor, remoteGit.Run("show", "refs/heads/data:metadata.json"));
    }

    [Fact]
    public void SuccessfulInputCanBeRetriedWithoutAnotherRevision()
    {
        var first = Publisher.Publish(preview, remote, NextExtractor, "456");
        var references = remoteGit.Run("show-ref");

        var retry = Publisher.Publish(preview, remote, InitialExtractor, "789");

        Assert.False(retry.Changed);
        Assert.Equal(first.Commit, retry.Commit);
        Assert.Equal(first.Tag, retry.Tag);
        Assert.Equal(references, remoteGit.Run("show-ref"));
    }

    [Theory]
    [InlineData("incomplete")]
    [InlineData("diagnostic")]
    [InlineData("count")]
    [InlineData("numeric-id")]
    [InlineData("overflow-id")]
    [InlineData("duplicate-id")]
    [InlineData("duplicate-locale")]
    [InlineData("duplicate-image")]
    [InlineData("dimensions")]
    [InlineData("missing-image")]
    [InlineData("corrupt-image")]
    [InlineData("extra-file")]
    [InlineData("unsupported-image")]
    [InlineData("image-traversal")]
    [InlineData("image-trailing-newline")]
    [InlineData("foreign-image-source")]
    [InlineData("metadata-only")]
    [InlineData("mismatched-resource")]
    public void InvalidPreviewLeavesAllRemoteReferencesUnchanged(string mutation)
    {
        Corrupt(mutation);
        AssertRejected();
    }

    [Theory]
    [InlineData("BBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBB", "456")]
    [InlineData(NextExtractor + "\n", "456")]
    [InlineData(NextExtractor, "0456")]
    [InlineData(NextExtractor, "0")]
    [InlineData(NextExtractor, "18446744073709551616")]
    public void InvalidProvenanceCannotChangeRemote(string commit, string manifest)
    {
        var before = remoteGit.Run("show-ref");
        Assert.Throws<InvalidDataException>(() => Publisher.Publish(preview, remote, commit, manifest));
        Assert.Equal(before, remoteGit.Run("show-ref"));
    }

    [Fact]
    public void RejectedTagPushIsAtomicAndIdenticalInputRetryWorks()
    {
        var hook = InstallHook("while read old new ref; do\n  case \"$ref\" in refs/tags/*) exit 1 ;; esac\ndone\nexit 0\n");
        var before = remoteGit.Run("show-ref");

        Assert.Throws<IOException>(() => Publisher.Publish(preview, remote, NextExtractor, "456"));
        Assert.Equal(before, remoteGit.Run("show-ref"));
        File.Delete(hook);

        var retry = Publisher.Publish(preview, remote, NextExtractor, "456");
        Assert.True(retry.Changed);
        Assert.Equal(retry.Commit, RemoteRef("refs/heads/data"));
        Assert.Equal(retry.Commit, RemoteRef("refs/tags/" + retry.Tag));
    }

    [Fact]
    public void ConcurrentDataWriterIsNeverOverwritten()
    {
        WritePreview(seed, "Concurrent writer");
        var seedGit = new Git(seed);
        Commit(seedGit);
        var competing = seedGit.Run("rev-parse", "HEAD").Trim();
        seedGit.Run("push", "--quiet", "origin", "HEAD:refs/heads/race-source");
        var tags = remoteGit.Run("show-ref", "--tags");
        InstallHook($"unset GIT_QUARANTINE_PATH\ngit update-ref refs/heads/data {competing} {initialCommit} || exit 1\nexit 0\n");

        Assert.Throws<IOException>(() => Publisher.Publish(preview, remote, NextExtractor, "456"));

        Assert.Equal(competing, RemoteRef("refs/heads/data"));
        Assert.Equal(initialCommit, RemoteRef("refs/heads/main"));
        Assert.Equal(tags, remoteGit.Run("show-ref", "--tags"));
    }

    private void Corrupt(string mutation)
    {
        switch (mutation)
        {
            case "incomplete": ChangeJson("coverage.json", n => n["status"] = "incomplete"); break;
            case "diagnostic": ChangeJson("coverage.json", n => n["issues"]!.AsArray().Add(new JsonObject { ["message"] = "failed" })); break;
            case "count": ChangeJson("coverage.json", n => n["images"] = 0); break;
            case "numeric-id": ChangeJson("assets.json", n => n[0]!["id"] = 42); break;
            case "overflow-id": ChangeJson("assets.json", n => n[0]!["id"] = "9223372036854775808"); break;
            case "duplicate-id": ChangeJson("assets.json", n => n.AsArray().Add(n[0]!.DeepClone())); break;
            case "duplicate-locale": ChangeJson("assets.json", n => n[0]!["text"]!.AsArray().Add(n[0]!["text"]![0]!.DeepClone())); break;
            case "duplicate-image": ChangeJson("assets.json", n => n[0]!["images"]!.AsArray().Add(n[0]!["images"]![0]!.DeepClone())); break;
            case "dimensions": ChangeJson("assets.json", n => n[0]!["images"]![0]!["width"] = 3); break;
            case "missing-image": File.Delete(Path.Combine(preview, Image)); break;
            case "corrupt-image": File.WriteAllBytes(Path.Combine(preview, Image), [137, 80, 78, 71, 13, 10, 26, 10]); break;
            case "extra-file": File.WriteAllText(Path.Combine(preview, "secret.txt"), "not allowed"); break;
            case "unsupported-image": ChangeJson("assets.json", n => n[0]!["images"]![0]!["status"] = "unsupported"); break;
            case "image-traversal": ChangeJson("assets.json", n => n[0]!["images"]![0]!["file"] = "../outside.png"); break;
            case "image-trailing-newline":
                File.Move(Path.Combine(preview, Image), Path.Combine(preview, Image + "\n"));
                ChangeJson("assets.json", n => n[0]!["images"]![0]!["file"] = Image + "\n");
                break;
            case "foreign-image-source": ChangeJson("assets.json", n => n[0]!["images"]![0]!["source"] = "/Game/Other.Other"); break;
            case "mismatched-resource": ChangeJson("assets.json", n => n[0]!["images"]![0]!["resource"] = "/Game/T_Other.T_Other"); break;
            case "metadata-only":
                ChangeJson("assets.json", n =>
                {
                    n[0]!["metadata"] = n[0]!["definitions"]!.DeepClone();
                    n[0]!["definitions"] = new JsonArray();
                });
                break;
            default: throw new ArgumentOutOfRangeException(nameof(mutation));
        }
    }

    private void ChangeJson(string file, Action<JsonNode> change)
    {
        var path = Path.Combine(preview, file);
        var value = JsonNode.Parse(File.ReadAllText(path))!;
        change(value);
        File.WriteAllText(path, value.ToJsonString());
    }

    private static void WritePreview(string directory, string name)
    {
        Directory.CreateDirectory(Path.Combine(directory, "images"));
        using var bitmap = new SKBitmap(2, 1);
        bitmap.Erase(SKColors.Red);
        using var image = SKImage.FromBitmap(bitmap);
        using var png = image.Encode(SKEncodedImageFormat.Png, 100);
        File.WriteAllBytes(Path.Combine(directory, Image), png.ToArray());
        var asset = new
        {
            id = "42",
            definitions = new[] { new { name = "DA_Test", @class = "PersistenceDataAsset", path = "/Game/DA_Test.DA_Test" } },
            metadata = Array.Empty<object>(),
            text = new[] { new { locale = "en", displayName = name, description = "Description" } },
            images = new[] { new { field = "Icon", source = "/Game/DA_Test.DA_Test", resource = Texture, status = "exported", file = Image, width = 2, height = 1 } },
            presentation = new
            {
                name = new { @namespace = "", key = "", source = name, cultureInvariant = true },
                description = new { @namespace = "", key = "", source = "Description", cultureInvariant = true },
                candidates = new[]
                {
                    new { role = "display-name", sourceKind = "definition", sourcePath = "/Game/DA_Test.DA_Test", sourceClass = "PersistenceDataAsset", field = "ItemName", definedAt = "/Game/DA_Test.DA_Test", reference = new { @namespace = "", key = "", source = name, cultureInvariant = true } },
                    new { role = "description", sourceKind = "definition", sourcePath = "/Game/DA_Test.DA_Test", sourceClass = "PersistenceDataAsset", field = "Description", definedAt = "/Game/DA_Test.DA_Test", reference = new { @namespace = "", key = "", source = "Description", cultureInvariant = true } }
                },
                containers = Array.Empty<object>()
            }
        };
        File.WriteAllBytes(Path.Combine(directory, "assets.json"), JsonSerializer.SerializeToUtf8Bytes(new[] { asset }, Preview.Json));
        File.WriteAllBytes(Path.Combine(directory, "coverage.json"), JsonSerializer.SerializeToUtf8Bytes(new
        {
            status = "succeeded",
            registeredAssets = 2,
            candidates = 2,
            loaded = 2,
            assetIds = 1,
            englishNames = 1,
            descriptions = 1,
            images = 1,
            issues = Array.Empty<object>(),
            notices = Array.Empty<object>(),
            discovery = new { nativeScope = "Tagged fields", mappingSha256 = new string('a', 64), objects = 2, resources = 1 }
        }, Preview.Json));
        File.WriteAllBytes(Path.Combine(directory, "resources.json"), JsonSerializer.SerializeToUtf8Bytes(new[]
        {
            new { path = Texture, status = "exported", file = Image, width = 2, height = 1 }
        }, Preview.Json));
        WriteLines(directory, "discovery/objects.jsonl.gz", new[] { "/Game/DA_Test.DA_Test", Texture }.Select(path => new
        {
            path,
            @class = path == Texture ? "Texture2D" : "PersistenceDataAsset",
            references = Array.Empty<object>(),
            texts = Array.Empty<object>(),
            values = Array.Empty<object>(),
            tableEntries = Array.Empty<object>(),
            issues = Array.Empty<object>()
        }));
        WriteLines(directory, "discovery/registry.jsonl.gz", new[]
        {
            new { path = "/Game/DA_Test.DA_Test", package = "/Game/DA_Test", @class = "PersistenceDataAsset", tags = new { } },
            new { path = Texture, package = "/Game/T_Test", @class = "Texture2D", tags = new { } }
        });
        WriteLines(directory, "discovery/files.jsonl.gz", new[]
        {
            new { path = "PioneerGame/Content/DA_Test.uasset", registryPackages = new[] { "/Game/DA_Test" } },
            new { path = "PioneerGame/Content/T_Test.uasset", registryPackages = new[] { "/Game/T_Test" } }
        });
        WriteLines(directory, "discovery/packages.jsonl.gz", new[]
        {
            new { path = "PioneerGame/Content/DA_Test.uasset", reason = "definition", status = "loaded", exports = 1, loaded = 1 },
            new { path = "PioneerGame/Content/T_Test.uasset", reason = "reference", status = "loaded", exports = 1, loaded = 1 }
        });
        WriteLines(directory, "localization/en.jsonl.gz", new[] { new { @namespace = "Shared", key = "UNCHANGED", value = "Unowned text" } });
    }

    private static void WriteLines<T>(string directory, string file, IEnumerable<T> rows)
    {
        var path = Path.Combine(directory, file);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        using var gzip = new GZipStream(File.Create(path), CompressionLevel.SmallestSize);
        using var writer = new StreamWriter(gzip);
        foreach (var row in rows) writer.WriteLine(JsonSerializer.Serialize(row));
    }

    private string InstallHook(string content)
    {
        var path = Path.Combine(remote, "hooks", "pre-receive");
        File.WriteAllText(path, "#!/bin/sh\n" + content);
        if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        return path;
    }

    private string RemoteRef(string reference) => remoteGit.Run("rev-parse", reference).Trim();

    private static void Commit(Git git)
    {
        git.Run("add", "--all", "--", ".");
        git.Run("-c", "user.name=Fixture", "-c", "user.email=fixture@example.invalid", "commit", "--quiet", "-m", "test: initialize snapshot");
    }

    public void Dispose() => Directory.Delete(root, recursive: true);
}
