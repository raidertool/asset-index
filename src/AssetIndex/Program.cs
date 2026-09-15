using System.Collections.Concurrent;
using CUE4Parse.FileProvider;
using CUE4Parse_Conversion.Textures;
using Serilog;
using Serilog.Core;
using Serilog.Events;

namespace AssetIndex;

internal static class Program
{
    private static readonly string[] Locales = ["en", "de", "es", "fr", "it", "ja", "ko", "pl", "pt_br", "ru", "tr", "zh_hans", "zh_hant"];

    public static int Main(string[] args)
    {
        if (args is ["--help"] or ["-h"])
        {
            Console.WriteLine(Options.Usage);
            return 0;
        }
        Options options;
        try
        {
            options = Options.Parse(args);
            options.Validate();
            Directory.CreateDirectory(options.OutputDirectory);
        }
        catch (Exception error)
        {
            Console.Error.WriteLine(error.Message);
            return 2;
        }

        var diagnostics = new ParserDiagnostics();
        using var logger = new LoggerConfiguration().MinimumLevel.Warning().WriteTo.Sink(diagnostics).CreateLogger();
        Log.Logger = logger;
        CUE4Parse.CUE4ParseLog.UseLogger(logger);
        CUE4Parse.Globals.FatalObjectSerializationErrors = true;
        TextureDecoder.UseAssetRipperTextureDecoder = true;
        var issues = new List<ExtractionIssue>();
        DiscoveryResult? discovery = null;
        var records = new List<AssetRecord>();
        try
        {
            Console.WriteLine("Mounting game containers...");
            using var provider = GameFiles.Open(options);
            Console.WriteLine($"Mounted {provider.Files.Count:N0} files. Reading asset registry and data assets...");
            discovery = AssetDiscovery.Read(provider);
            issues.AddRange(discovery.Issues);
            records = ExportAssets(discovery.Assets, provider, options.OutputDirectory, issues);
            if (records.Count == 0)
                issues.Add(new("validation", "assets", "No asset IDs were extracted."));
        }
        catch (Exception error)
        {
            issues.Add(new("fatal", "extraction", error.Message));
        }
        issues.AddRange(diagnostics.Issues.Distinct());
        var report = new RunReport(issues.Count == 0 ? "succeeded" : "incomplete",
            discovery?.RegisteredAssets ?? 0, discovery?.Candidates ?? 0, discovery?.Loaded ?? 0, records.Count,
            records.Count(asset => asset.Text.Any(text => text.Locale == "en" && text.DisplayName.Length > 0)),
            records.Count(asset => asset.Text.Any(text => text.Locale == "en" && text.Description.Length > 0)),
            records.Count(asset => asset.Images.Any(image => image.Status == "exported")), issues);
        Snapshot.Write(options.OutputDirectory, "assets.json", records);
        Snapshot.Write(options.OutputDirectory, "coverage.json", report);
        Console.WriteLine($"{report.Status}: {report.AssetIds:N0} IDs, {report.EnglishNames:N0} English names, {report.Images:N0} assets with images, {issues.Count:N0} issues.");
        return issues.Count == 0 ? 0 : 1;
    }

    private static List<AssetRecord> ExportAssets(IReadOnlyList<CatalogAsset> assets, IFileProvider provider,
        string output, List<ExtractionIssue> issues)
    {
        Console.WriteLine($"Found {assets.Count:N0} IDs. Reading text...");
        var texts = assets.Select(asset => Text.Read(asset, issues)).ToArray();
        var localized = Text.Localize(provider, texts, Locales, issues).ToLookup(row => row.AssetId);
        Console.WriteLine("Exporting referenced images...");
        var records = new List<AssetRecord>();
        foreach (var asset in assets)
        {
            records.Add(new(asset.Id, asset.Name, asset.Definition.ExportType, asset.Definition.GetPathName(),
                asset.Metadata.Select(source => source.GetPathName()).ToArray(), localized[asset.Id].ToArray(),
                Images.Export(asset, output, issues)));
            if (records.Count % 250 == 0)
                Console.WriteLine($"Processed {records.Count:N0}/{assets.Count:N0} assets.");
        }
        return records;
    }

    private sealed class ParserDiagnostics : ILogEventSink
    {
        public ConcurrentQueue<ExtractionIssue> Issues { get; } = new();
        public void Emit(LogEvent logEvent) => Issues.Enqueue(new("parser", logEvent.Level.ToString(),
            logEvent.RenderMessage() + (logEvent.Exception is null ? "" : " " + logEvent.Exception.Message)));
    }
}
