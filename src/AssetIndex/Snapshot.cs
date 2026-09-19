using System.Text.Json;
using System.IO.Compression;

namespace AssetIndex;

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
        var path = OutputPath(output, name);
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

    internal static string OutputPath(string output, string name)
    {
        if (Path.IsPathRooted(name) || name.Contains('\\') || name.Split('/').Any(part => part is "" or "." or ".."))
            throw new InvalidDataException("Snapshot filenames must be relative paths inside the output directory.");
        return Path.Combine(output, name);
    }
}

// A stream becomes a snapshot file only after the caller completes every row.
internal sealed class JsonLinesFile<T> : IDisposable
{
    private readonly string path;
    private readonly string temporary;
    private readonly GZipStream stream;

    public JsonLinesFile(string output, string name)
    {
        path = Snapshot.OutputPath(output, name);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        // Discovery evidence is intermediate output; public locale files keep their existing encoding.
        stream = new GZipStream(new FileStream(temporary, FileMode.CreateNew, FileAccess.Write), CompressionLevel.Fastest);
    }

    public void Write(T row)
    {
        JsonSerializer.Serialize(stream, row, Snapshot.CompactJson);
        stream.WriteByte((byte)'\n');
    }

    public void Complete()
    {
        stream.Dispose();
        File.Move(temporary, path);
    }

    public void Dispose()
    {
        stream.Dispose();
        File.Delete(temporary);
    }
}
