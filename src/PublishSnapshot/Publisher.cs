using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace PublishSnapshot;

internal sealed record Publication(bool Changed, string Commit, string Tag);
internal sealed record SteamMetadata(int AppId, int DepotId, string ManifestId);
internal sealed record Metadata(int FormatVersion, string ContentSha256, string ExtractorCommit, SteamMetadata Steam)
{
    public static Metadata Create(string extractorCommit, string manifestId, string contentSha256)
    {
        ValidateProvenance(extractorCommit, manifestId);
        Preview.Require(Regex.IsMatch(contentSha256, @"\A[0-9a-f]{64}\z", RegexOptions.CultureInvariant), "Invalid snapshot content digest.");
        return new(2, contentSha256, extractorCommit, new(1808500, 1808501, manifestId));
    }

    public static void ValidateProvenance(string extractorCommit, string manifestId)
    {
        Preview.Require(Regex.IsMatch(extractorCommit, @"\A[0-9a-f]{40}\z", RegexOptions.CultureInvariant), "Extractor commit must be 40 lowercase hexadecimal characters.");
        Preview.Require(ulong.TryParse(manifestId, NumberStyles.None, CultureInfo.InvariantCulture, out var manifest) && manifest > 0 && manifest.ToString(CultureInfo.InvariantCulture) == manifestId, "Manifest ID must be a canonical positive uint64 string.");
    }

    public static Metadata Parse(string json)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        Preview.Fields(root, "formatVersion", "contentSha256", "extractorCommit", "steam");
        var steam = root.GetProperty("steam");
        Preview.Fields(steam, "appId", "depotId", "manifestId");
        Preview.Require(root.GetProperty("formatVersion").GetInt32() == 2 && steam.GetProperty("appId").GetInt32() == 1808500 && steam.GetProperty("depotId").GetInt32() == 1808501, "Existing snapshot metadata is incompatible.");
        return Create(Preview.String(root, "extractorCommit"), Preview.String(steam, "manifestId"), Preview.String(root, "contentSha256"));
    }
}

internal static class Publisher
{
    public static Publication Publish(string previewDirectory, string remote, string extractorCommit, string manifestId)
    {
        Metadata.ValidateProvenance(extractorCommit, manifestId);
        using var preview = Preview.Read(previewDirectory);
        using var snapshot = DataSnapshot.Create(preview);
        var metadata = Metadata.Create(extractorCommit, manifestId, ContentDigest.Files(snapshot.Files));
        var directory = Path.Combine(Path.GetTempPath(), "asset-index-publish-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try { return PublishSnapshot(directory, remote, snapshot.Files, metadata); }
        finally { Directory.Delete(directory, recursive: true); }
    }

    public static Publication PublishExport(string exportDirectory, string remote, string extractorCommit,
        string manifestId, string exportSha256, string expectedMetadataBlob)
    {
        Metadata.ValidateProvenance(extractorCommit, manifestId);
        Preview.Require(Regex.IsMatch(exportSha256, @"\A[0-9a-f]{64}\z", RegexOptions.CultureInvariant), "Invalid export digest.");
        Preview.Require(expectedMetadataBlob == "missing" ||
            Regex.IsMatch(expectedMetadataBlob, @"\A[0-9a-f]{40}\z", RegexOptions.CultureInvariant),
            "Expected metadata blob must be a Git SHA-1 or missing.");
        using var export = PublicExport.Read(exportDirectory, exportSha256);
        Preview.Require(export.Metadata == Metadata.Create(extractorCommit, manifestId, export.Metadata.ContentSha256),
            "Export metadata differs from the trusted extraction result.");
        var directory = Path.Combine(Path.GetTempPath(), "asset-index-publish-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try { return PublishSnapshot(directory, remote, export.Files, export.Metadata, expectedMetadataBlob); }
        finally { Directory.Delete(directory, recursive: true); }
    }

    public static Metadata Export(string previewDirectory, string outputDirectory, string extractorCommit, string manifestId)
    {
        Metadata.ValidateProvenance(extractorCommit, manifestId);
        using var preview = Preview.Read(previewDirectory);
        using var snapshot = DataSnapshot.Create(preview);
        var metadata = Metadata.Create(extractorCommit, manifestId, ContentDigest.Files(snapshot.Files));
        var output = Path.GetFullPath(outputDirectory);
        Preview.Require(!Path.Exists(output), "Export destination must not exist.");
        var parent = Path.GetDirectoryName(output)!;
        Preview.Require(Directory.Exists(parent), "Export destination parent must exist.");
        Preview.Require((File.GetAttributes(parent) & FileAttributes.ReparsePoint) == 0, "Export destination parent cannot be a link.");
        var staging = Path.Combine(parent, ".asset-index-export-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(staging);
        try
        {
            WriteSnapshot(staging, snapshot.Files, metadata);
            Directory.Move(staging, output);
        }
        finally { if (Directory.Exists(staging)) Directory.Delete(staging, recursive: true); }
        return metadata;
    }

    private static Publication PublishSnapshot(string directory, string remote,
        IReadOnlyDictionary<string, SnapshotFile> files, Metadata metadata, string? expectedMetadataBlob = null)
    {
        var git = new Git(directory);
        git.Run("init", "--quiet");
        git.Run("remote", "add", "origin", remote);
        git.Run("fetch", "--quiet", "--no-tags", "--depth", "1", "origin", "refs/heads/data");
        var parent = git.Run("rev-parse", "FETCH_HEAD").Trim();
        var entries = GeneratedTree(git, parent);
        git.Run("checkout", "--quiet", "--detach", parent);
        var tag = Tag(metadata.Steam.ManifestId, metadata.ContentSha256);
        var existing = git.Run("ls-remote", "--refs", "origin", "refs/tags/" + tag);
        if (existing.Length > 0)
        {
            git.Run("fetch", "--quiet", "--no-tags", "--depth", "1", "origin", "refs/tags/" + tag);
            var commit = git.Run("rev-parse", "FETCH_HEAD").Trim();
            Preview.Require(git.Run("cat-file", "-t", commit).Trim() == "commit", "Release tag must point directly to a commit.");
            var previous = Metadata.Parse(git.Run("show", commit + ":metadata.json"));
            Preview.Require(previous.ContentSha256 == metadata.ContentSha256 && previous.Steam == metadata.Steam &&
                ContentDigest.Tree(GeneratedTree(git, commit)) == metadata.ContentSha256,
                "Existing release tag conflicts with the validated snapshot.");
            Preview.Require(ContentDigest.Tree(entries) == metadata.ContentSha256 &&
                Metadata.Parse(git.Run("show", parent + ":metadata.json")) == previous,
                "Data branch no longer contains this release; refusing to restore an older snapshot.");
            return new(false, commit, tag);
        }
        if (expectedMetadataBlob is not null)
            Preview.Require((entries.SingleOrDefault(entry => entry.Path == "metadata.json")?.ObjectId ?? "missing") == expectedMetadataBlob,
                "Published metadata changed since extraction was planned; retry against the current snapshot.");
        foreach (var entry in entries) File.Delete(Path.Combine(directory, entry.Path));
        WriteSnapshot(directory, files, metadata);
        var expectedMetadata = git.Run("hash-object", "--no-filters", "coverage.json", "metadata.json");
        var roots = entries.Select(entry => entry.Path).Concat(files.Keys).Append("metadata.json")
            .Select(path => path.Split('/')[0]).Distinct(StringComparer.Ordinal).ToArray();
        git.Run(["add", "--force", "--all", "--", .. roots]);
        var staged = git.Run("write-tree").Trim();
        var stagedEntries = GeneratedTree(git, staged);
        Preview.Require(ContentDigest.Tree(stagedEntries) == metadata.ContentSha256,
            "Git transformed generated content; publication was not attempted.");
        var stagedMetadata = string.Concat(new[] { "coverage.json", "metadata.json" }
            .Select(path => stagedEntries.Single(entry => entry.Path == path).ObjectId + "\n"));
        Preview.Require(stagedMetadata == expectedMetadata,
            "Git transformed generated metadata; publication was not attempted.");
        if (git.Run("diff", "--cached", "--name-only").Length > 0)
            git.Run("-c", "user.name=alexbowe", "-c", "user.email=alex@alexbowe.com", "commit", "--quiet", "-m", "chore: update asset snapshot");
        var published = git.Run("rev-parse", "HEAD").Trim();
        git.Run("push", "--atomic", "origin", $"{published}:refs/heads/data", $"{published}:refs/tags/{tag}");
        return new(true, published, tag);
    }

    private static void WriteSnapshot(string directory, IReadOnlyDictionary<string, SnapshotFile> files, Metadata metadata)
    {
        foreach (var (path, file) in files)
        {
            var target = Path.Combine(directory, path);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(file.Path, target);
        }
        if (!files.ContainsKey("metadata.json"))
            File.WriteAllBytes(Path.Combine(directory, "metadata.json"), JsonSerializer.SerializeToUtf8Bytes(metadata, Preview.Json));
    }

    private static ContentDigest.Entry[] GeneratedTree(Git git, string commit)
    {
        var result = new List<ContentDigest.Entry>();
        foreach (var entry in git.Run("ls-tree", "-r", "-z", commit).Split('\0', StringSplitOptions.RemoveEmptyEntries))
        {
            var parts = entry.Split('\t', 2);
            Preview.Require(parts.Length == 2, "Invalid Git tree entry.");
            var path = parts[1];
            if (!Reserved(path)) continue;
            var header = parts[0].Split(' ');
            Preview.Require(header.Length == 3 && header[0] == "100644" && header[1] == "blob" &&
                (DataSnapshot.Allowed(path) || LegacyGenerated(path)), "Generated paths must contain ordinary allowlisted files only.");
            result.Add(new(header[0], header[2], path));
        }
        return result.ToArray();
    }

    private static bool Reserved(string path) => DataSnapshot.Required.Contains(path) || path is "metadata.json" or "schema.json" or "images" or "localization" ||
        path.StartsWith("images/", StringComparison.Ordinal) || path.StartsWith("localization/", StringComparison.Ordinal);

    private static bool LegacyGenerated(string path) => path is "metadata.json" or "schema.json" ||
        Regex.IsMatch(path, @"\Aimages/[A-Za-z0-9_-]+\.png\z", RegexOptions.CultureInvariant);

    internal static string Tag(string manifest, string contentSha256) => $"arc-{manifest}-{contentSha256[..12]}";
}

internal sealed class Git(string directory)
{
    public string Run(params string[] arguments)
    {
        var start = new ProcessStartInfo("git")
        {
            WorkingDirectory = directory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };
        start.ArgumentList.Add("-c");
        start.ArgumentList.Add("commit.gpgsign=false");
        foreach (var argument in arguments) start.ArgumentList.Add(argument);
        start.Environment["GIT_TERMINAL_PROMPT"] = "0";
        using var process = Process.Start(start) ?? throw new IOException("Could not start Git.");
        var output = process.StandardOutput.ReadToEndAsync();
        var errors = process.StandardError.ReadToEndAsync();
        if (!process.WaitForExit(300_000))
        {
            process.Kill(entireProcessTree: true);
            process.WaitForExit();
            throw new IOException("Git operation timed out.");
        }
        Task.WaitAll(output, errors);
        // Remote/server error text can contain credentials or arbitrary hook output.
        if (process.ExitCode != 0) throw new IOException($"Git operation failed (exit {process.ExitCode}); no forced update was attempted.");
        return output.Result;
    }
}
