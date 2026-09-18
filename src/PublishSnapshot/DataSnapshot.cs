using AssetIndex;
using System.Text.Json;

namespace PublishSnapshot;

// Projection borrows validated files and owns its public coverage and resource list.
// Keep the Preview alive until this snapshot has been published and disposed.
internal sealed class DataSnapshot : IDisposable
{
    internal const long MaximumBlobBytes = 100L * 1024 * 1024;
    internal static readonly string[] Required = ["asset_index.csv", "asset_localizations.csv", "assets.json", "coverage.json", "resources.json", "localization/en.jsonl.gz"];
    private readonly string directory = Path.Combine(Path.GetTempPath(), "asset-index-data-" + Guid.NewGuid().ToString("N"));
    public IReadOnlyDictionary<string, SnapshotFile> Files { get; private set; } = new Dictionary<string, SnapshotFile>();

    private DataSnapshot() { }

    public static bool Allowed(string path) => Required.Contains(path) || ResourceFiles.IsImagePath(path) || SnapshotFiles.LocalePath.IsMatch(path);
    public static bool Payload(string path) => Allowed(path) && path != "coverage.json";

    public static DataSnapshot Create(Preview preview)
    {
        var snapshot = new DataSnapshot();
        Directory.CreateDirectory(snapshot.directory);
        try
        {
            using var inventory = Preview.ReadJson(preview.Files, "resources.json");
            var selected = inventory.RootElement.EnumerateArray()
                .OrderBy(resource => Preview.String(resource, "path"), StringComparer.Ordinal).ToArray();
            var files = preview.Files.Where(pair => pair.Key is "assets.json" or "asset_index.csv" or "asset_localizations.csv" || SnapshotFiles.LocalePath.IsMatch(pair.Key))
                .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
            foreach (var resource in selected)
            {
                var path = Preview.String(resource, "file");
                files.Add(path, preview.Files[path]);
            }
            var sorted = Path.Combine(snapshot.directory, "resources.json");
            File.WriteAllBytes(sorted, JsonSerializer.SerializeToUtf8Bytes(selected, Preview.Json));
            files.Add("resources.json", SnapshotFile.Read(sorted));
            var coverage = Path.Combine(snapshot.directory, "coverage.json");
            File.WriteAllBytes(coverage, PublicCoverage(preview));
            files.Add("coverage.json", SnapshotFile.Read(coverage));
            foreach (var (path, file) in files)
                Preview.Require(file.Length <= MaximumBlobBytes, $"Published file exceeds 100 MiB: {path}");
            snapshot.Files = files;
            return snapshot;
        }
        catch { snapshot.Dispose(); throw; }
    }

    private static byte[] PublicCoverage(Preview preview)
    {
        using var report = Preview.ReadJson(preview.Files, "coverage.json");
        var source = report.RootElement;
        var discovery = source.GetProperty("discovery");
        var result = new Dictionary<string, object>
        {
            ["formatVersion"] = 3,
            ["status"] = "succeeded"
        };
        foreach (var field in new[] { "registeredAssets", "candidates", "loaded", "assetIds", "englishNames", "descriptions", "images" })
            result.Add(field, source.GetProperty(field).GetInt32());
        result.Add("issueCounts", new { total = 0 });
        result.Add("noticeCount", source.GetProperty("notices").GetArrayLength());
        result.Add("exploration", new
        {
            unavailableSoftReferences = Count("unavailableSoftReferences"),
            unavailableHardReferences = Count("unavailableHardReferences"),
            unmappedNonCatalogExports = Count("unmappedNonCatalogExports")
        });
        result.Add("discovery", new
        {
            mappingSha256 = Preview.String(discovery, "mappingSha256"),
            objects = discovery.GetProperty("objects").GetInt32(),
            resources = discovery.GetProperty("resources").GetInt32()
        });
        return JsonSerializer.SerializeToUtf8Bytes(result, Preview.Json);
        int Count(string field) => discovery.TryGetProperty(field, out var value) ? value.GetInt32() : 0;
    }

    public void Dispose() => Directory.Delete(directory, recursive: true);
}
