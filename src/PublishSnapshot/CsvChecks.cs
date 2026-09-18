using System.Text.Json;
using AssetIndex;

namespace PublishSnapshot;

internal static class CsvChecks
{
    public static void Validate(JsonElement assets, IReadOnlyDictionary<string, SnapshotFile> files)
    {
        var records = assets.Deserialize<AssetRecord[]>(Preview.Json)!;
        Matches(files[CatalogCsv.MainFile], CatalogCsv.MainRows(records));
        Matches(files[CatalogCsv.LocalizationFile], CatalogCsv.LocalizationRows(records));
    }

    private static void Matches(SnapshotFile file, IEnumerable<string[]> rows)
    {
        using var expected = new MemoryStream();
        CatalogCsv.Write(expected, rows);
        Preview.Require(expected.Length == file.Length &&
            System.Security.Cryptography.SHA256.HashData(expected.ToArray()).AsSpan().SequenceEqual(file.Hash),
            $"CSV differs from the validated catalog: {Path.GetFileName(file.Path)}.");
    }
}
