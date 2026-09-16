using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace PublishSnapshot;

internal sealed record Publication(bool Changed, string Commit, string Tag);
internal sealed record SteamMetadata(int AppId, int DepotId, string ManifestId);
internal sealed record Metadata(int FormatVersion, string ExtractorCommit, SteamMetadata Steam)
{
    public static Metadata Create(string extractorCommit, string manifestId)
    {
        Preview.Require(Regex.IsMatch(extractorCommit, "\\A[0-9a-f]{40}\\z", RegexOptions.CultureInvariant), "Extractor commit must be 40 lowercase hexadecimal characters.");
        Preview.Require(ulong.TryParse(manifestId, NumberStyles.None, CultureInfo.InvariantCulture, out var manifest) && manifest > 0 && manifest.ToString(CultureInfo.InvariantCulture) == manifestId, "Manifest ID must be a canonical positive uint64 string.");
        return new(1, extractorCommit, new(1808500, 1808501, manifestId));
    }

    public static Metadata Read(string path)
    {
        using var document = JsonDocument.Parse(File.ReadAllBytes(path));
        var root = document.RootElement;
        Preview.Fields(root, "formatVersion", "extractorCommit", "steam");
        var steam = root.GetProperty("steam");
        Preview.Fields(steam, "appId", "depotId", "manifestId");
        Preview.Require(root.GetProperty("formatVersion").GetInt32() == 1 && steam.GetProperty("appId").GetInt32() == 1808500 && steam.GetProperty("depotId").GetInt32() == 1808501, "Existing snapshot metadata is incompatible.");
        return Create(Preview.String(root, "extractorCommit"), Preview.String(steam, "manifestId"));
    }
}

internal static class Publisher
{
    public static Publication Publish(string previewDirectory, string remote, string extractorCommit, string manifestId) =>
        Run(previewDirectory, remote, extractorCommit, manifestId, initialize: false);

    public static Publication Initialize(string previewDirectory, string remote, string extractorCommit, string manifestId) =>
        Run(previewDirectory, remote, extractorCommit, manifestId, initialize: true);

    private static Publication Run(string previewDirectory, string remote, string extractorCommit, string manifestId, bool initialize)
    {
        // Capture and validate private files before any Git operation.
        var metadata = Metadata.Create(extractorCommit, manifestId);
        using var preview = Preview.Read(previewDirectory);
        using var snapshot = DataSnapshot.Create(preview);
        var directory = Path.Combine(Path.GetTempPath(), "asset-index-publish-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var git = new Git(directory);
            git.Run("init", "--quiet");
            git.Run("remote", "add", "origin", remote);
            if (initialize)
            {
                Preview.Require(git.Run("ls-remote", "--refs", "origin", "refs/heads/data").Length == 0,
                    "Data branch already exists; use normal publication.");
                git.Run("symbolic-ref", "HEAD", "refs/heads/data");
            }
            else
            {
                git.Run("fetch", "--quiet", "--no-tags", "--depth", "1", "origin", "refs/heads/data");
                var parent = git.Run("rev-parse", "FETCH_HEAD").Trim();
                var paths = ValidateTree(git, parent);
                git.Run("checkout", "--quiet", "--detach", parent);
                if (SameContent(directory, paths, snapshot.Files))
                {
                    var previous = Metadata.Read(Path.Combine(directory, "metadata.json"));
                    var tag = Tag(previous.Steam.ManifestId, parent);
                    var reference = git.Run("ls-remote", "--refs", "origin", "refs/tags/" + tag).Split('\t', '\n');
                    Preview.Require(reference.Length >= 2 && reference[0] == parent && reference[1] == "refs/tags/" + tag, "Unchanged snapshot lacks its exact lightweight tag.");
                    return new(false, parent, tag);
                }
                git.Run("rm", "--quiet", "-r", "--ignore-unmatch", "--", ".");
            }
            foreach (var (path, file) in snapshot.Files)
            {
                var target = Path.Combine(directory, path);
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                File.Copy(file.Path, target);
            }
            File.WriteAllBytes(Path.Combine(directory, "metadata.json"), JsonSerializer.SerializeToUtf8Bytes(metadata, Preview.Json));
            git.Run("add", "--all", "--", ".");
            git.Run("-c", "user.name=Asset Index", "-c", "user.email=asset-index@users.noreply.github.com", "commit", "--quiet", "-m",
                initialize ? "chore: initialize asset snapshot" : "chore: update asset snapshot");
            var commit = git.Run("rev-parse", "HEAD").Trim();
            var newTag = Tag(manifestId, commit);
            if (initialize)
                // Empty expected values reject conflicting ref creation. Git may
                // safely complete the tag if a racer created this exact commit.
                git.Run("push", "--atomic", "--force-with-lease=refs/heads/data:", $"--force-with-lease=refs/tags/{newTag}:",
                    "origin", $"{commit}:refs/heads/data", $"{commit}:refs/tags/{newTag}");
            else
                git.Run("push", "--atomic", "origin", $"{commit}:refs/heads/data", $"{commit}:refs/tags/{newTag}");
            return new(true, commit, newTag);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static string[] ValidateTree(Git git, string commit)
    {
        var entries = git.Run("ls-tree", "-r", "-z", commit).Split('\0', StringSplitOptions.RemoveEmptyEntries);
        var paths = new List<string>();
        foreach (var entry in entries)
        {
            var parts = entry.Split('\t', 2);
            Preview.Require(parts.Length == 2 && parts[0].StartsWith("100644 blob ", StringComparison.Ordinal), "Data branch must contain ordinary generated files only.");
            var path = parts[1];
            Preview.Require(path == "metadata.json" || DataSnapshot.Allowed(path), "Data branch contains a file outside the published snapshot.");
            paths.Add(path);
        }
        Preview.Require(DataSnapshot.Required.Append("metadata.json").All(paths.Contains), "Existing data branch is not an initialized snapshot.");
        return paths.ToArray();
    }

    private static bool SameContent(string directory, IEnumerable<string> existing, IReadOnlyDictionary<string, SnapshotFile> incoming)
    {
        var paths = existing.Where(DataSnapshot.Payload).ToHashSet(StringComparer.Ordinal);
        return paths.SetEquals(incoming.Keys.Where(DataSnapshot.Payload)) && paths.All(path => incoming[path].Matches(Path.Combine(directory, path)));
    }

    internal static string Tag(string manifest, string commit) => $"arc-{manifest}-{commit[..12]}";
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
