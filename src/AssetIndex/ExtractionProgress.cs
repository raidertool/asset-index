using System.Text.Json;

namespace AssetIndex;

// Sample only the active operation's coordinates, never the object being decoded.
internal sealed class ExtractionProgress : IDisposable
{
    private readonly TextWriter output;
    private readonly TimeProvider clock;
    private readonly ITimer timer;
    private Operation? current;

    public ExtractionProgress(TextWriter output, TimeProvider? clock = null)
    {
        this.output = output;
        this.clock = clock ?? TimeProvider.System;
        timer = this.clock.CreateTimer(_ => Sample(), null, TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(30));
    }

    public void Set(string phase, string? package = null, int? exportIndex = null) =>
        Volatile.Write(ref current, new(phase, package, exportIndex, clock.GetTimestamp()));

    private void Sample()
    {
        var operation = Volatile.Read(ref current);
        if (operation is null) return;
        var sample = new
        {
            operation.Phase,
            operation.Package,
            operation.ExportIndex,
            ElapsedSeconds = Math.Round(clock.GetElapsedTime(operation.Started).TotalSeconds, 1)
        };
        try { output.WriteLine(JsonSerializer.Serialize(sample, Snapshot.CompactJson)); }
        catch (IOException) { } // A diagnostic writer must not terminate extraction from a timer callback.
        catch (ObjectDisposedException) { }
    }

    public void Dispose() => timer.DisposeAsync().AsTask().GetAwaiter().GetResult();

    private sealed record Operation(string Phase, string? Package, int? ExportIndex, long Started);
}
