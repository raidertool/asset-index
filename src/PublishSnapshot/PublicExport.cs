using System.Text.Json;
using System.Text.RegularExpressions;
using static PublishSnapshot.Preview;

namespace PublishSnapshot;

// Original game-object evidence is checked before export. This handoff binds the
// public files to that trusted job's digest and checks their public contract again.
internal sealed record PublicExport(SnapshotFiles Snapshot, Metadata Metadata, string ExportSha256) : IDisposable
{
    public IReadOnlyDictionary<string, SnapshotFile> Files => Snapshot.Files;

    public static PublicExport Read(string directory, string? expectedExportSha256 = null)
    {
        var snapshot = SnapshotFiles.CapturePublic(directory);
        try
        {
            foreach (var (path, file) in snapshot.Files)
                Require(file.Length <= DataSnapshot.MaximumBlobBytes, $"Published file exceeds 100 MiB: {path}");
            var exportSha256 = ContentDigest.ExportFiles(snapshot.Files);
            Require(expectedExportSha256 is null || exportSha256 == expectedExportSha256,
                "Export digest differs from the trusted extraction result.");
            var metadata = Metadata.Parse(File.ReadAllText(snapshot.Files["metadata.json"].Path));
            Require(ContentDigest.Files(snapshot.Files) == metadata.ContentSha256, "Export content digest differs from its metadata.");
            using var assets = ReadJson(snapshot.Files, "assets.json");
            using var coverage = ReadJson(snapshot.Files, "coverage.json");
            ValidateCoverage(coverage.RootElement);
            var localizations = ResourceEvidence.ReadLocalizations(snapshot.Files);
            var resources = ResourceEvidence.ReadResources(snapshot.Files, static _ => true);
            // Only the upstream full preview can establish original object existence.
            ValidateCatalog(assets.RootElement, coverage.RootElement, localizations, resources, static _ => true);
            CheckCount(coverage.RootElement.GetProperty("discovery"), "resources", resources.Count);
            CsvChecks.Validate(assets.RootElement, snapshot.Files);
            return new(snapshot, metadata, exportSha256);
        }
        catch { snapshot.Dispose(); throw; }
    }

    private static void ValidateCoverage(JsonElement report)
    {
        Fields(report, "formatVersion", "status", "registeredAssets", "candidates", "loaded", "assetIds", "englishNames", "descriptions", "images", "issueCounts", "noticeCount", "exploration", "discovery");
        Require(report.GetProperty("formatVersion").GetInt32() == 3 && String(report, "status") == "succeeded", "Export coverage is incompatible or incomplete.");
        Fields(report.GetProperty("issueCounts"), "total");
        CheckCount(report.GetProperty("issueCounts"), "total", 0);
        Require(report.GetProperty("noticeCount").GetInt32() >= 0, "Invalid notice count.");
        var exploration = report.GetProperty("exploration");
        string[] counts = ["unavailableSoftReferences", "unavailableHardReferences", "unmappedNonCatalogExports"];
        Fields(exploration, counts);
        foreach (var field in counts) Require(exploration.GetProperty(field).GetInt32() >= 0, "Invalid exploration count.");
        var discovery = report.GetProperty("discovery");
        Fields(discovery, "mappingSha256", "objects", "resources");
        Require(Regex.IsMatch(String(discovery, "mappingSha256"), "\\A[0-9a-f]{64}\\z", RegexOptions.CultureInvariant), "Invalid mapping hash.");
        Require(discovery.GetProperty("objects").GetInt32() > 0, "Invalid object count.");
    }

    public void Dispose() => Snapshot.Dispose();
}
