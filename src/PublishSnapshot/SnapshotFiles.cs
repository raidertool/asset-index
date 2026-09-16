using System.Security.Cryptography;
using System.Text.RegularExpressions;

namespace PublishSnapshot;

internal sealed record SnapshotFile(string Path, long Length, byte[] Hash)
{
    public static SnapshotFile Read(string path)
    {
        using var stream = File.OpenRead(path);
        return new(path, stream.Length, SHA256.HashData(stream));
    }

    public bool Matches(string path)
    {
        using var stream = File.OpenRead(path);
        return stream.Length == Length && SHA256.HashData(stream).AsSpan().SequenceEqual(Hash);
    }
}

// A private copy retains exactly the files that passed validation without keeping
// every PNG or compressed discovery stream in memory.
internal sealed class SnapshotFiles : IDisposable
{
    private readonly string directory = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "asset-index-preview-" + Guid.NewGuid().ToString("N"));
    public Dictionary<string, SnapshotFile> Files { get; } = new(StringComparer.Ordinal);
    internal static readonly Regex ImagePath = new("\\Aimages/[0-9a-f]{64}\\.png\\z", RegexOptions.CultureInvariant);
    internal static readonly Regex LocalePath = new("\\Alocalization/[a-z]{2,3}(?:_[a-z0-9]{2,8})*\\.jsonl\\.gz\\z", RegexOptions.CultureInvariant);
    internal static readonly string[] Required = ["assets.json", "coverage.json", "resources.json", "discovery/objects.jsonl.gz", "discovery/exports.jsonl.gz", "discovery/files.jsonl.gz", "discovery/registry.jsonl.gz", "discovery/packages.jsonl.gz", "localization/en.jsonl.gz"];

    public static bool Allowed(string path) => Required.Contains(path) || ImagePath.IsMatch(path) || LocalePath.IsMatch(path);
    public static SnapshotFiles Capture(string source)
    {
        var snapshot = new SnapshotFiles();
        Directory.CreateDirectory(snapshot.directory);
        try
        {
            snapshot.CopyDirectory(System.IO.Path.GetFullPath(source), "");
            foreach (var path in Required) Preview.Require(snapshot.Files.ContainsKey(path), $"Missing preview file: {path}");
            return snapshot;
        }
        catch { snapshot.Dispose(); throw; }
    }

    private void CopyDirectory(string source, string prefix)
    {
        Preview.Require((File.GetAttributes(source) & FileAttributes.ReparsePoint) == 0, "Preview directories cannot be links.");
        foreach (var entry in Directory.EnumerateFileSystemEntries(source).Order(StringComparer.Ordinal))
        {
            var attributes = File.GetAttributes(entry);
            Preview.Require((attributes & FileAttributes.ReparsePoint) == 0, "Preview files cannot be links.");
            var relative = prefix + System.IO.Path.GetFileName(entry);
            if ((attributes & FileAttributes.Directory) != 0)
            {
                Preview.Require(relative is "images" or "discovery" or "localization", $"Unexpected preview directory: {relative}");
                CopyDirectory(entry, relative + "/");
                continue;
            }
            Preview.Require(Allowed(relative), $"Unexpected preview file: {relative}");
            var target = System.IO.Path.Combine(directory, relative);
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(target)!);
            File.Copy(entry, target);
            Files.Add(relative, SnapshotFile.Read(target));
        }
    }

    public void Dispose() => Directory.Delete(directory, recursive: true);
}
