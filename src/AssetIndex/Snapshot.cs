using System.Text.Json;

namespace AssetIndex;

internal sealed record AssetRecord(long Id, string Name, string Class, string Path,
    IReadOnlyList<string> Metadata, IReadOnlyList<LocalizedText> Text, IReadOnlyList<AssetImage> Images);

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
