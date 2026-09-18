using System.Text.Json;

namespace PublishSnapshot;

// Projection borrows validated files; only the sorted resource list is owned here.
// Keep the Preview alive until this snapshot has been published and disposed.
internal sealed class DataSnapshot : IDisposable
{
    internal const long MaximumBlobBytes = 100L * 1024 * 1024;
    internal static readonly string[] Required = ["assets.json", "coverage.json", "resources.json", "localization/en.jsonl.gz"];
    private readonly string directory = Path.Combine(Path.GetTempPath(), "asset-index-data-" + Guid.NewGuid().ToString("N"));
    public IReadOnlyDictionary<string, SnapshotFile> Files { get; private set; } = new Dictionary<string, SnapshotFile>();

    private DataSnapshot() { }

    public static bool Allowed(string path) => Required.Contains(path) || SnapshotFiles.ImagePath.IsMatch(path) || SnapshotFiles.LocalePath.IsMatch(path);
    public static bool Payload(string path) => path is not ("coverage.json" or "metadata.json");

    public static DataSnapshot Create(Preview preview)
    {
        var snapshot = new DataSnapshot();
        Directory.CreateDirectory(snapshot.directory);
        try
        {
            using var inventory = Preview.ReadJson(preview.Files, "resources.json");
            var selected = inventory.RootElement.EnumerateArray()
                .OrderBy(resource => Preview.String(resource, "path"), StringComparer.Ordinal).ToArray();
            var files = preview.Files.Where(pair => pair.Key is "assets.json" or "coverage.json" || SnapshotFiles.LocalePath.IsMatch(pair.Key))
                .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
            foreach (var resource in selected)
            {
                var path = Preview.String(resource, "file");
                files.Add(path, preview.Files[path]);
            }
            var sorted = Path.Combine(snapshot.directory, "resources.json");
            File.WriteAllBytes(sorted, JsonSerializer.SerializeToUtf8Bytes(selected, Preview.Json));
            files.Add("resources.json", SnapshotFile.Read(sorted));
            foreach (var (path, file) in files)
                Preview.Require(file.Length <= MaximumBlobBytes, $"Published file exceeds 100 MiB: {path}");
            snapshot.Files = files;
            return snapshot;
        }
        catch { snapshot.Dispose(); throw; }
    }

    public void Dispose() => Directory.Delete(directory, recursive: true);
}
