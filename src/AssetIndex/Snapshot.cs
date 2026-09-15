using System.Text.Json;
using System.Text.Json.Serialization;
using System.IO.Compression;

namespace AssetIndex;

internal sealed record ObjectReference(string Name, string Class, string Path);
internal sealed record Translation(string Locale, string DisplayName, string Description);
internal sealed record AssetRecord(
    [property: JsonNumberHandling(JsonNumberHandling.WriteAsString)] long Id,
    IReadOnlyList<ObjectReference> Definitions, IReadOnlyList<ObjectReference> Metadata,
    IReadOnlyList<Translation> Text, IReadOnlyList<AssetImage> Images);

internal static class Snapshot
{
    internal static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };

    public static void Write<T>(string output, string name, T value)
    {
        WriteFile(output, name, stream => JsonSerializer.Serialize(stream, value, Json));
    }

    public static void WriteFile(string output, string name, Action<Stream> write)
    {
        var path = Path.Combine(output, name);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write)) write(stream);
            File.Move(temporary, path); // Never replace an existing snapshot file.
        }
        finally { File.Delete(temporary); }
    }

    public static void WriteLines<T>(string output, string name, IEnumerable<T> rows) =>
        WriteFile(output, name, stream =>
        {
            using var compressed = new GZipStream(stream, CompressionLevel.SmallestSize, leaveOpen: true);
            using var writer = new StreamWriter(compressed);
            foreach (var row in rows) writer.WriteLine(JsonSerializer.Serialize(row, CompactJson));
        });

    internal static readonly JsonSerializerOptions CompactJson = new(Json) { WriteIndented = false };
}

// Streaming object evidence avoids retaining a second object graph in memory.
internal sealed class EvidenceFile : IDisposable
{
    private readonly string path;
    private readonly string temporary;
    private readonly StreamWriter writer;

    public EvidenceFile(string output)
    {
        path = Path.Combine(output, "discovery", "objects.jsonl.gz");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        writer = new StreamWriter(new GZipStream(new FileStream(temporary, FileMode.CreateNew, FileAccess.Write), CompressionLevel.SmallestSize));
    }

    public void Write(Discovery.ObjectEvidence evidence) => writer.WriteLine(JsonSerializer.Serialize(evidence, Snapshot.CompactJson));

    public void Complete()
    {
        writer.Dispose();
        File.Move(temporary, path);
    }

    public void Dispose()
    {
        writer.Dispose();
        File.Delete(temporary);
    }
}
