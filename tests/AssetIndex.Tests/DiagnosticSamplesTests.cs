using System.Text.Json;

namespace AssetIndex.Tests;

public class DiagnosticSamplesTests
{
    [Fact]
    public void RepeatedAndExcessDiagnosticsDoNotFloodTheLog()
    {
        using var output = new StringWriter();
        var samples = new DiagnosticSamples(output);
        var issue = new ExtractionIssue("evidence", "/Game/A.Value", "Unsupported field.\nDetails");
        for (var index = 0; index < 1_000; index++) samples.Write(issue, "same");
        for (var index = 0; index < 1_000; index++) samples.Write(issue, index.ToString());

        var lines = output.ToString().Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal(100, lines.Length);
        using var first = JsonDocument.Parse(lines[0]);
        Assert.Equal(issue.Message, first.RootElement.GetProperty("message").GetString());
    }
}
