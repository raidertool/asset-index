using System.Collections.Concurrent;
using System.Security.Cryptography;
using CUE4Parse.FileProvider;
using CUE4Parse.UE4.Assets.Exports.Texture;
using CUE4Parse_Conversion.Textures;
using Serilog;
using Serilog.Core;
using Serilog.Events;

namespace AssetIndex;

internal static class Program
{
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

        try { return Run(options); }
        catch (Exception error)
        {
            Console.Error.WriteLine($"Could not write extraction output: {error.Message}");
            return 1;
        }
    }

    private static int Run(Options options)
    {
        var diagnostics = new ParserDiagnostics();
        using var logger = new LoggerConfiguration().MinimumLevel.Warning().WriteTo.Sink(diagnostics).CreateLogger();
        Log.Logger = logger;
        CUE4Parse.CUE4ParseLog.UseLogger(logger);
        CUE4Parse.Globals.FatalObjectSerializationErrors = true;
        TextureDecoder.UseAssetRipperTextureDecoder = true;
        var issues = new List<ExtractionIssue>();
        DiscoveryResult? discovery = null;
        var records = new List<AssetRecord>();
        var textureCount = 0;
        try
        {
            Console.WriteLine("Mounting game containers...");
            using var provider = GameFiles.Open(options);
            Console.WriteLine($"Mounted {provider.Files.Count:N0} files. Reading typed object fields and references...");
            using var evidence = new EvidenceFile(options.OutputDirectory);
            discovery = AssetDiscovery.Read(provider, evidence.Write);
            evidence.Complete();
            issues.AddRange(discovery.Issues);
            Snapshot.WriteLines(options.OutputDirectory, "discovery/registry.jsonl.gz", discovery.Registry);
            Snapshot.WriteLines(options.OutputDirectory, "discovery/packages.jsonl.gz", discovery.Packages);
            var resources = new TextureResources(options.OutputDirectory, issues);
            Console.WriteLine("Exporting UI and referenced textures...");
            foreach (var texture in discovery.Objects.OfType<UTexture2D>()) resources.Export(texture);
            records = ExportAssets(discovery.Assets, provider, resources, issues);
            Snapshot.Write(options.OutputDirectory, "resources.json", resources.Entries);
            textureCount = resources.Entries.Count;
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
            records.Count(asset => asset.Images.Any(image => image.Status == "exported")), issues,
            diagnostics.Notices.Distinct().ToArray(), new(Discovery.EvidenceReader.NativeScope,
                Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(options.Usmap))), discovery?.Objects.Count ?? 0, textureCount));
        Snapshot.Write(options.OutputDirectory, "assets.json", records);
        Snapshot.Write(options.OutputDirectory, "coverage.json", report);
        Console.WriteLine($"{report.Status}: {report.AssetIds:N0} IDs, {report.EnglishNames:N0} English names, {report.Images:N0} assets with images, {issues.Count:N0} issues.");
        return issues.Count == 0 ? 0 : 1;
    }

    private static List<AssetRecord> ExportAssets(IReadOnlyList<CatalogAsset> assets, IFileProvider provider,
        TextureResources resources, List<ExtractionIssue> issues)
    {
        Console.WriteLine($"Found {assets.Count:N0} IDs. Reading text...");
        var texts = assets.Select(asset => Text.Read(asset, issues)).ToArray();
        var localized = Text.Localize(provider, texts, issues).ToLookup(row => row.AssetId);
        Console.WriteLine("Exporting referenced images...");
        var records = new List<AssetRecord>();
        foreach (var asset in assets)
        {
            records.Add(new(asset.Id,
                asset.Definitions.Select(source => new ObjectReference(source.Name, source.ExportType, source.GetPathName())).ToArray(),
                asset.Metadata.Select(source => new ObjectReference(source.Name, source.ExportType, source.GetPathName())).ToArray(),
                localized[asset.Id].Select(text => new Translation(text.Locale, text.DisplayName, text.Description)).ToArray(),
                Images.Export(asset, resources, issues)));
            if (records.Count % 250 == 0)
                Console.WriteLine($"Processed {records.Count:N0}/{assets.Count:N0} assets.");
        }
        return records;
    }

    internal sealed class ParserDiagnostics : ILogEventSink
    {
        public ConcurrentQueue<ExtractionIssue> Issues { get; } = new();
        public ConcurrentQueue<ExtractionIssue> Notices { get; } = new();
        public void Emit(LogEvent logEvent)
        {
            var issue = new ExtractionIssue("parser", logEvent.Level.ToString(),
                logEvent.RenderMessage() + (logEvent.Exception is null ? "" : " " + logEvent.Exception.Message));
            // CUE labels an already-root mount as strange before normalizing it to root.
            // Retain that observation; GameFiles separately checks that containers mounted.
            if (logEvent.Exception is null && logEvent.Level == LogEventLevel.Warning &&
                logEvent.MessageTemplate.Text == "\"{Name}\" has strange mount point \"{MountPoint}\", mounting to root" &&
                logEvent.Properties.GetValueOrDefault("MountPoint") is ScalarValue { Value: "/" })
                Notices.Enqueue(issue);
            else
                Issues.Enqueue(issue);
        }
    }
}
