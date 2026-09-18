using System.Text.Json.Nodes;

namespace PublishSnapshot.Tests;

public sealed partial class PublisherTests
{
    [Fact]
    public void PublicExportPublishesWithoutDiscoveryAndPreservesSource()
    {
        File.WriteAllText(Path.Combine(seed, "README.md"), "Source stays on main");
        var git = new Git(seed);
        Commit(git);
        git.Run("push", "--quiet", "origin", "HEAD:refs/heads/main");
        var previous = MetadataBlob();
        var (directory, metadata) = Handoff();
        var input = HashFiles(directory);
        Assert.False(Directory.Exists(Path.Combine(directory, "discovery")));

        var result = Publisher.PublishExport(directory, remote, NextExtractor, "456", HandoffDigest(directory), previous);

        Assert.True(result.Changed);
        Assert.Equal(metadata, ReadMetadata(result.Commit));
        Assert.Equal("Source stays on main", remoteGit.Run("show", result.Commit + ":README.md"));
        Assert.Equal(input, HashFiles(directory));
    }

    [Fact]
    public void PublicExportOfANewSteamReleaseKeepsIdenticalPayloadIdentity()
    {
        WritePreview(preview, "Initial name");
        var (directory, metadata) = Handoff();

        var result = Publisher.PublishExport(directory, remote, NextExtractor, "456", HandoffDigest(directory), MetadataBlob());

        Assert.True(result.Changed);
        Assert.Equal(initialDigest, metadata.ContentSha256);
        Assert.Equal(Publisher.Tag("456", initialDigest), result.Tag);
        Assert.NotEqual(initialCommit, result.Commit);
    }

    [Fact]
    public void PublicExportRetryIgnoresTheOldPlanningBlobAndPreservesLaterSourceCommit()
    {
        var previous = MetadataBlob();
        var (directory, metadata) = Handoff();
        var first = Publisher.PublishExport(directory, remote, NextExtractor, "456", HandoffDigest(directory), previous);
        var git = new Git(seed);
        git.Run("fetch", "--quiet", "origin", "refs/heads/main");
        git.Run("checkout", "--quiet", "--detach", "FETCH_HEAD");
        File.WriteAllText(Path.Combine(seed, "README.md"), "Later source update");
        Commit(git);
        git.Run("push", "--quiet", "origin", "HEAD:refs/heads/main");
        var references = remoteGit.Run("show-ref");

        var retry = Publisher.PublishExport(directory, remote, NextExtractor, "456", HandoffDigest(directory), previous);

        Assert.False(retry.Changed);
        Assert.Equal(first.Commit, retry.Commit);
        Assert.Equal(references, remoteGit.Run("show-ref"));
    }

    [Fact]
    public void PublicExportRejectsAChangedPublishedSnapshot()
    {
        var previous = MetadataBlob();
        var (directory, metadata) = Handoff();
        Publisher.Publish(preview, remote, InitialExtractor, "789");
        var references = remoteGit.Run("show-ref");

        var error = Assert.Throws<InvalidDataException>(() =>
            Publisher.PublishExport(directory, remote, NextExtractor, "456", HandoffDigest(directory), previous));

        Assert.Contains("Published metadata changed", error.Message);
        Assert.Equal(references, remoteGit.Run("show-ref"));
    }

    [Fact]
    public void PublicExportCanBootstrapAnExplicitlyMissingMetadataFile()
    {
        File.Delete(Path.Combine(seed, "metadata.json"));
        var git = new Git(seed);
        Commit(git);
        git.Run("push", "--quiet", "origin", "HEAD:refs/heads/main");
        var (directory, metadata) = Handoff();

        var result = Publisher.PublishExport(directory, remote, NextExtractor, "456", HandoffDigest(directory), "missing");

        Assert.True(result.Changed);
        Assert.Equal(metadata, ReadMetadata(result.Commit));
    }

    [Theory]
    [InlineData("wrong-source")]
    [InlineData("wrong-steam")]
    [InlineData("wrong-digest")]
    [InlineData("invalid-plan-blob")]
    [InlineData("extra-file")]
    [InlineData("discovery-directory")]
    [InlineData("missing-file")]
    [InlineData("changed-payload")]
    [InlineData("changed-coverage")]
    [InlineData("private-coverage")]
    [InlineData("valid-coverage-change")]
    [InlineData("metadata-bytes")]
    [InlineData("file-link")]
    [InlineData("directory-link")]
    [InlineData("root-link")]
    public void PublicExportRejectsTamperingBeforeTouchingGit(string mutation)
    {
        var (directory, metadata) = Handoff();
        var source = NextExtractor;
        var manifest = "456";
        var digest = HandoffDigest(directory);
        var previous = MetadataBlob();
        switch (mutation)
        {
            case "wrong-source": source = InitialExtractor; break;
            case "wrong-steam": manifest = "789"; break;
            case "wrong-digest": digest = new string('0', 64); break;
            case "invalid-plan-blob": previous = "main"; break;
            case "extra-file": File.WriteAllText(Path.Combine(directory, "private.txt"), "private"); break;
            case "discovery-directory": Directory.CreateDirectory(Path.Combine(directory, "discovery")); break;
            case "missing-file": File.Delete(Path.Combine(directory, "assets.json")); break;
            case "changed-payload": File.AppendAllText(Path.Combine(directory, "assets.json"), " "); break;
            case "changed-coverage": ChangeExportJson(directory, "coverage.json", json => json["images"] = 0); break;
            case "valid-coverage-change": ChangeExportJson(directory, "coverage.json", json => json["noticeCount"] = 1); break;
            case "metadata-bytes": File.AppendAllText(Path.Combine(directory, "metadata.json"), " "); break;
            case "private-coverage": ChangeExportJson(directory, "coverage.json", json => json["privateDetails"] = "private"); break;
            case "file-link":
                File.Delete(Path.Combine(directory, "assets.json"));
                File.CreateSymbolicLink(Path.Combine(directory, "assets.json"), Path.Combine(preview, "assets.json"));
                break;
            case "directory-link":
                Directory.Delete(Path.Combine(directory, "images"), recursive: true);
                Directory.CreateSymbolicLink(Path.Combine(directory, "images"), Path.Combine(preview, "images"));
                break;
            case "root-link":
                Directory.CreateSymbolicLink(Path.Combine(root, "linked-export"), directory);
                directory = Path.Combine(root, "linked-export");
                break;
        }
        var references = remoteGit.Run("show-ref");

        Assert.Throws<InvalidDataException>(() => Publisher.PublishExport(directory,
            Path.Combine(root, "nonexistent-remote"), source, manifest, digest, previous));

        Assert.Equal(references, remoteGit.Run("show-ref"));
    }

    [Theory]
    [InlineData("assets.json", "/0")]
    [InlineData("assets.json", "/0/definitions/0")]
    [InlineData("assets.json", "/0/metadata/0")]
    [InlineData("assets.json", "/0/text/0")]
    [InlineData("assets.json", "/0/images/0")]
    [InlineData("assets.json", "/0/presentation")]
    [InlineData("assets.json", "/0/presentation/name")]
    [InlineData("assets.json", "/0/presentation/description")]
    [InlineData("assets.json", "/0/presentation/candidates/0")]
    [InlineData("assets.json", "/0/presentation/candidates/0/reference")]
    [InlineData("resources.json", "/0")]
    public void PublicExportChecksItsSchemaEvenWithAMatchingDigest(string file, string pointer)
    {
        var (directory, metadata) = Handoff();
        ChangeExportJson(directory, file, json =>
        {
            var node = json;
            foreach (var part in pointer.Split('/', StringSplitOptions.RemoveEmptyEntries))
                node = (int.TryParse(part, out var index) ? node[index] : node[part])!;
            node.AsObject().Add("privateDetails", "private");
        });
        var digest = ContentDigest.Files(Directory.GetFiles(directory, "*", SearchOption.AllDirectories)
            .ToDictionary(path => Path.GetRelativePath(directory, path).Replace('\\', '/'), SnapshotFile.Read));
        ChangeExportJson(directory, "metadata.json", json => json["contentSha256"] = digest);
        var references = remoteGit.Run("show-ref");

        var error = Assert.Throws<InvalidDataException>(() => Publisher.PublishExport(directory,
            Path.Combine(root, "nonexistent-remote"), NextExtractor, "456", HandoffDigest(directory), MetadataBlob()));

        Assert.Contains("JSON fields do not match", error.Message);
        Assert.Equal(references, remoteGit.Run("show-ref"));
    }

    [Fact]
    public void PublicExportCommandRequiresTheGuardAndPublishesWithIt()
    {
        var (directory, metadata) = Handoff();
        var previous = MetadataBlob();
        Assert.Equal(2, Program.Main(["--publish-export", directory, remote, NextExtractor, "456", HandoffDigest(directory)]));
        Assert.Equal(0, Program.Main(["--export-digest", directory]));
        using var exported = PublicExport.Read(directory);
        Assert.Equal(HandoffDigest(directory), exported.ExportSha256);

        var exit = Program.Main(["--publish-export", directory, remote, NextExtractor, "456", HandoffDigest(directory), previous]);

        Assert.Equal(0, exit);
        Assert.Equal(metadata, ReadMetadata(RemoteRef("refs/heads/main")));
    }

    [Theory]
    [InlineData("coverage.json")]
    [InlineData("metadata.json")]
    public void PublicExportDigestIncludesBytesExcludedFromDatasetIdentity(string file)
    {
        var (directory, metadata) = Handoff();
        var original = HandoffDigest(directory);
        File.AppendAllText(Path.Combine(directory, file), " ");
        using var changed = SnapshotFiles.CapturePublic(directory);

        Assert.NotEqual(original, ContentDigest.ExportFiles(changed.Files));
        Assert.Equal(metadata.ContentSha256, ContentDigest.Files(changed.Files));
        Assert.Equal(ContentDigest.ExportFiles(changed.Files),
            ContentDigest.ExportFiles(changed.Files.Reverse().ToDictionary(pair => pair.Key, pair => pair.Value)));
    }

    [Fact]
    public void PublicExportKeepsACapturedCopyAfterIncomingFilesChange()
    {
        var (directory, metadata) = Handoff();
        using var captured = PublicExport.Read(directory, HandoffDigest(directory));
        File.AppendAllText(Path.Combine(directory, "assets.json"), " ");

        Assert.False(captured.Files["assets.json"].Matches(Path.Combine(directory, "assets.json")));
        Assert.Equal(metadata.ContentSha256, ContentDigest.Files(captured.Files));
    }

    [Fact]
    public void PublicExportPreservesTheExactValidatedMetadataBytes()
    {
        var (directory, metadata) = Handoff();
        var path = Path.Combine(directory, "metadata.json");
        File.AppendAllText(path, " ");

        var result = Publisher.PublishExport(directory, remote, NextExtractor, "456", HandoffDigest(directory), MetadataBlob());

        Assert.Equal(metadata, ReadMetadata(result.Commit));
        Assert.Equal(File.ReadAllText(path), remoteGit.Run("show", result.Commit + ":metadata.json"));
    }

    [Fact]
    public void PublicExportRejectsGitTransformingCoverageAfterValidation()
    {
        File.WriteAllText(Path.Combine(seed, ".gitattributes"), "coverage.json text eol=lf\n");
        var git = new Git(seed);
        Commit(git);
        git.Run("push", "--quiet", "origin", "HEAD:refs/heads/main");
        var (directory, _) = Handoff();
        var coverage = Path.Combine(directory, "coverage.json");
        File.WriteAllText(coverage, File.ReadAllText(coverage).Replace("\n", "\r\n"));
        var references = remoteGit.Run("show-ref");

        var error = Assert.Throws<InvalidDataException>(() => Publisher.PublishExport(directory, remote,
            NextExtractor, "456", HandoffDigest(directory), MetadataBlob()));

        Assert.Contains("Git transformed generated metadata", error.Message);
        Assert.Equal(references, remoteGit.Run("show-ref"));
    }

    [Fact]
    public void PublicExportFailedAtomicPushCanBeRetried()
    {
        var previous = MetadataBlob();
        var (directory, metadata) = Handoff();
        var hook = InstallHook("case \"$1\" in refs/tags/*) exit 1 ;; esac\nexit 0\n", "update");
        var references = remoteGit.Run("show-ref");

        Assert.Throws<IOException>(() => Publisher.PublishExport(directory, remote, NextExtractor, "456", HandoffDigest(directory), previous));
        Assert.Equal(references, remoteGit.Run("show-ref"));
        File.Delete(hook);

        Assert.True(Publisher.PublishExport(directory, remote, NextExtractor, "456", HandoffDigest(directory), previous).Changed);
    }

    private (string Directory, Metadata Metadata) Handoff()
    {
        var directory = Path.Combine(root, "public-export");
        return (directory, Publisher.Export(preview, directory, NextExtractor, "456"));
    }

    private static string HandoffDigest(string directory)
    {
        using var captured = SnapshotFiles.CapturePublic(directory);
        return ContentDigest.ExportFiles(captured.Files);
    }

    private string MetadataBlob() => remoteGit.Run("rev-parse", "refs/heads/main:metadata.json").Trim();

    private static void ChangeExportJson(string directory, string file, Action<JsonNode> change)
    {
        var path = Path.Combine(directory, file);
        var json = JsonNode.Parse(File.ReadAllText(path))!;
        change(json);
        File.WriteAllText(path, json.ToJsonString());
    }
}
