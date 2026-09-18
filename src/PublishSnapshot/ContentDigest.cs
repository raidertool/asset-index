using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace PublishSnapshot;

// Hash the generated Git blob inventory so consumers can verify a release from
// its pinned Git tree without downloading every image. Ordering is .NET Ordinal.
internal static class ContentDigest
{
    internal sealed record Entry(string Mode, string ObjectId, string Path);

    public static string Files(IReadOnlyDictionary<string, SnapshotFile> files) => Calculate(files
        .Where(pair => DataSnapshot.Payload(pair.Key))
        .Select(pair => new Entry("100644", BlobId(pair.Value), pair.Key)));

    public static string Tree(IEnumerable<Entry> entries) => Calculate(entries.Where(entry => DataSnapshot.Payload(entry.Path)));

    // The job handoff includes exact coverage and metadata bytes. Dataset identity
    // intentionally excludes those files so diagnostic changes do not version data.
    public static string ExportFiles(IReadOnlyDictionary<string, SnapshotFile> files) => Calculate(files
        .Select(pair => new Entry("100644", BlobId(pair.Value), pair.Key)), "asset-index-export-v1\n");

    private static string Calculate(IEnumerable<Entry> entries, string prefix = "asset-index-content-v1\n")
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        hash.AppendData(Encoding.UTF8.GetBytes(prefix));
        foreach (var entry in entries.OrderBy(entry => entry.Path, StringComparer.Ordinal))
            hash.AppendData(Encoding.UTF8.GetBytes($"{entry.Mode} {entry.ObjectId}\t{entry.Path}\n"));
        return Convert.ToHexStringLower(hash.GetHashAndReset());
    }

    private static string BlobId(SnapshotFile file)
    {
        using var stream = File.OpenRead(file.Path);
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA1);
        hash.AppendData(Encoding.ASCII.GetBytes($"blob {stream.Length.ToString(CultureInfo.InvariantCulture)}\0"));
        var buffer = new byte[81920];
        int count;
        while ((count = stream.Read(buffer)) > 0) hash.AppendData(buffer.AsSpan(0, count));
        return Convert.ToHexStringLower(hash.GetHashAndReset());
    }
}
