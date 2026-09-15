using System.Text.Json;

namespace AssetIndex.Tests;

public sealed class ExtractionProgressTests
{
    [Fact]
    public void TimerReportsTheCurrentOperationAndItsOwnElapsedTime()
    {
        using var output = new StringWriter();
        var clock = new ManualTimeProvider();
        using var progress = new ExtractionProgress(output, clock);
        progress.Set("decode-export", "Plugin/Asset.uasset", 7);

        clock.Advance(29);
        Assert.Equal("", output.ToString());
        clock.Advance(1);
        using var first = JsonDocument.Parse(output.ToString());
        Assert.Equal("decode-export", first.RootElement.GetProperty("phase").GetString());
        Assert.Equal("Plugin/Asset.uasset", first.RootElement.GetProperty("package").GetString());
        Assert.Equal(7, first.RootElement.GetProperty("exportIndex").GetInt32());
        Assert.Equal(30, first.RootElement.GetProperty("elapsedSeconds").GetDouble());

        output.GetStringBuilder().Clear();
        clock.Advance(5);
        progress.Set("write-evidence", "Plugin/Asset.uasset", 8);
        clock.Advance(25);
        using var second = JsonDocument.Parse(output.ToString());
        Assert.Equal("write-evidence", second.RootElement.GetProperty("phase").GetString());
        Assert.Equal(8, second.RootElement.GetProperty("exportIndex").GetInt32());
        Assert.Equal(25, second.RootElement.GetProperty("elapsedSeconds").GetDouble());
    }

    [Fact]
    public void IdleAndDisposedTimersDoNotReportStaleOperations()
    {
        using var output = new StringWriter();
        var clock = new ManualTimeProvider();
        var progress = new ExtractionProgress(output, clock);
        clock.Advance(30);
        Assert.Equal("", output.ToString());

        progress.Set("catalog");
        progress.Dispose();
        clock.Advance(60);
        Assert.Equal("", output.ToString());
    }

    [Fact]
    public void ChangingToAPackagePhaseClearsThePreviousExport()
    {
        using var output = new StringWriter();
        var clock = new ManualTimeProvider();
        using var progress = new ExtractionProgress(output, clock);
        progress.Set("decode-export", "Plugin/First.uasset", 9);
        progress.Set("load-package", "Plugin/Second.uasset");
        clock.Advance(30);

        using var sample = JsonDocument.Parse(output.ToString());
        Assert.Equal("Plugin/Second.uasset", sample.RootElement.GetProperty("package").GetString());
        Assert.Equal(JsonValueKind.Null, sample.RootElement.GetProperty("exportIndex").ValueKind);
    }

    [Fact]
    public void DiagnosticWriteFailureCannotEscapeTheTimerCallback()
    {
        var clock = new ManualTimeProvider();
        using var progress = new ExtractionProgress(new FailingWriter(), clock);
        progress.Set("decode-export");
        clock.Advance(30);
    }

    private sealed class FailingWriter : StringWriter
    {
        public override void WriteLine(string? value) => throw new IOException("Closed diagnostic pipe.");
    }

    internal sealed class ManualTimeProvider : TimeProvider
    {
        private long timestamp;
        private ManualTimer? timer;
        public override long TimestampFrequency => TimeSpan.TicksPerSecond;
        public override long GetTimestamp() => timestamp;
        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period) =>
            timer = new(this, callback, state, dueTime, period);

        public void Advance(int seconds)
        {
            timestamp += TimeSpan.FromSeconds(seconds).Ticks;
            timer?.Tick();
        }

        private sealed class ManualTimer : ITimer
        {
            private readonly ManualTimeProvider clock;
            private readonly TimerCallback callback;
            private readonly object? state;
            private long due;
            private long period;

            public ManualTimer(ManualTimeProvider clock, TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
            {
                this.clock = clock;
                this.callback = callback;
                this.state = state;
                Change(dueTime, period);
            }

            public bool Change(TimeSpan dueTime, TimeSpan interval)
            {
                due = clock.timestamp + dueTime.Ticks;
                period = interval.Ticks;
                return true;
            }

            public void Tick()
            {
                if (clock.timestamp < due) return;
                due = clock.timestamp + period;
                callback(state);
            }

            public void Dispose() => due = long.MaxValue;
            public ValueTask DisposeAsync()
            {
                Dispose();
                return ValueTask.CompletedTask;
            }
        }
    }
}
