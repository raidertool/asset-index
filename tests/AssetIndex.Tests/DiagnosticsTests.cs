using Serilog;

namespace AssetIndex.Tests;

public sealed class DiagnosticsTests
{
    [Fact]
    public void OnlyTheAlreadyRootMountNoticeIsNonfatal()
    {
        var diagnostics = new Program.ParserDiagnostics();
        using var logger = new LoggerConfiguration().WriteTo.Sink(diagnostics).CreateLogger();
        logger.Warning("\"{Name}\" has strange mount point \"{MountPoint}\", mounting to root", "root.pak", "/");
        logger.Warning("\"{Name}\" has strange mount point \"{MountPoint}\", mounting to root", "broken.pak", "../unknown");
        logger.Warning("Failed to read a property.");
        Assert.Single(diagnostics.Notices);
        Assert.Equal(2, diagnostics.Issues.Count);
    }
}
