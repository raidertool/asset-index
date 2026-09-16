using System.Text.Json;
using System.Text.Json.Serialization;
using System.IO.Compression;

namespace AssetIndex;

internal sealed record ObjectReference(string Name, string Class, string Path);
internal sealed record Translation(string Locale, string DisplayName, string Description);
internal sealed record ContainerPresentation(string Role, string ContainerType, string FramePath,
    int ContainerIndex, string SlotPath, string? ContainerPath, string MetadataPath);
internal sealed record VisualSlotMemberPresentation(string ItemPath, string MetadataPath);
internal sealed record VisualSlotPresentation(string SlotPath, string TypeTag, string MetadataPath,
    IReadOnlyList<VisualSlotMemberPresentation> Members);
internal sealed record AssetPresentation(TextReference? Name, TextReference? Description,
    IReadOnlyList<TextCandidate> Candidates, IReadOnlyList<ContainerPresentation> Containers)
{
    public IReadOnlyList<VisualSlotPresentation> VisualSlots { get; init; } = [];
}
internal sealed record AssetRecord(
    [property: JsonNumberHandling(JsonNumberHandling.WriteAsString)] long Id,
    IReadOnlyList<ObjectReference> Definitions, IReadOnlyList<ObjectReference> Metadata,
    IReadOnlyList<Translation> Text, IReadOnlyList<AssetImage> Images)
{
    public AssetPresentation Presentation { get; init; } = new(null, null, [], []);
}

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
        stream = new GZipStream(new FileStream(temporary, FileMode.CreateNew, FileAccess.Write), CompressionLevel.SmallestSize);
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
