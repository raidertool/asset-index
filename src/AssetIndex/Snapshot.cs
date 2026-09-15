using System.Text.Json;
using System.Text.Json.Serialization;

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
        using var file = File.Create(Path.Combine(output, name));
        JsonSerializer.Serialize(file, value, Json);
    }
}
