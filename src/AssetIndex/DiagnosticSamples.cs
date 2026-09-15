using System.Text.Json;

namespace AssetIndex;

// Keep interrupted runs diagnosable without printing thousands of repeated fields.
internal sealed class DiagnosticSamples(TextWriter output)
{
    private readonly HashSet<string> categories = new(StringComparer.Ordinal);

    public void Write(ExtractionIssue issue, string category)
    {
        lock (categories)
        {
            if (categories.Count >= 100 || !categories.Add(category)) return;
            output.WriteLine(JsonSerializer.Serialize(issue, Snapshot.CompactJson));
        }
    }
}
