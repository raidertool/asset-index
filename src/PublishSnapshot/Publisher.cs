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
    public static Publication Publish(string previewDirectory, string remote, string extractorCommit, string manifestId)
    {
        // Retain the validated bytes: later input changes cannot alter the published snapshot.
        var metadata = Metadata.Create(extractorCommit, manifestId);
        var preview = Preview.Read(previewDirectory);
        var directory = Path.Combine(Path.GetTempPath(), "asset-index-publish-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var git = new Git(directory);
            git.Run("init", "--quiet");
            git.Run("remote", "add", "origin", remote);
            git.Run("fetch", "--quiet", "--no-tags", "origin", "refs/heads/data");
            var parent = git.Run("rev-parse", "FETCH_HEAD").Trim();
            var paths = ValidateTree(git, parent);
            git.Run("checkout", "--quiet", "--detach", parent);
            if (SameContent(directory, paths, preview.Files))
            {
                var previous = Metadata.Read(Path.Combine(directory, "metadata.json"));
                var tag = Tag(previous.Steam.ManifestId, parent);
                var reference = git.Run("ls-remote", "--refs", "origin", "refs/tags/" + tag).Split('\t', '\n');
                Preview.Require(reference.Length >= 2 && reference[0] == parent && reference[1] == "refs/tags/" + tag, "Unchanged snapshot lacks its exact lightweight tag.");
                return new(false, parent, tag);
            }
            git.Run("rm", "--quiet", "-r", "--ignore-unmatch", "--", ".");
            foreach (var (path, bytes) in preview.Files)
            {
                var target = Path.Combine(directory, path);
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                File.WriteAllBytes(target, bytes);
            }
            File.WriteAllBytes(Path.Combine(directory, "metadata.json"), JsonSerializer.SerializeToUtf8Bytes(metadata, Preview.Json));
            git.Run("add", "--all", "--", ".");
            git.Run("-c", "user.name=Asset Index", "-c", "user.email=asset-index@users.noreply.github.com", "commit", "--quiet", "-m", "chore: update asset snapshot");
            var commit = git.Run("rev-parse", "HEAD").Trim();
            var newTag = Tag(manifestId, commit);
            // Atomic, ordinary ref updates: a competing data commit or tag rejects the whole push.
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
            Preview.Require(path is "assets.json" or "coverage.json" or "metadata.json" || Regex.IsMatch(path, "\\Aimages/[0-9a-f]{64}\\.png\\z", RegexOptions.CultureInvariant), "Data branch contains a file outside the generated snapshot.");
            paths.Add(path);
        }
        Preview.Require(new[] { "assets.json", "coverage.json", "metadata.json" }.All(paths.Contains), "Existing data branch is not an initialized snapshot.");
        return paths.ToArray();
    }

    private static bool SameContent(string directory, IEnumerable<string> existing, IReadOnlyDictionary<string, byte[]> incoming)
    {
        static bool Payload(string path) => path == "assets.json" || path.StartsWith("images/", StringComparison.Ordinal);
        var paths = existing.Where(Payload).ToHashSet(StringComparer.Ordinal);
        return paths.SetEquals(incoming.Keys.Where(Payload)) && paths.All(path => File.ReadAllBytes(Path.Combine(directory, path)).AsSpan().SequenceEqual(incoming[path]));
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
