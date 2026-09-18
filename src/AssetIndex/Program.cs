using System.Collections.Concurrent;
using System.Security.Cryptography;
using CUE4Parse.UE4.Assets.Exports.Texture;
using CUE4Parse.UE4.Objects.Engine;
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
        var notices = new List<ExtractionIssue>();
        DiscoveryResult? discovery = null;
        var records = new List<AssetRecord>();
        var resourceCount = 0;
        IReadOnlyList<InputContainer>? inputContainers = null;
        try
        {
            Console.WriteLine("Mounting game containers...");
            using var provider = GameFiles.Open(options);
            inputContainers = Discovery.PackageInputIndex.Census(provider);
            Console.WriteLine($"Mounted {provider.Files.Count:N0} files. Reading typed object fields and references...");
            var storage = PackageStorage.Read(provider.Files.Values);
            Console.WriteLine($"Mounted IoStore packages including shadowed versions: {storage.Packages:N0} distinct entries, {storage.Bytes:N0} raw bytes.");
            Console.WriteLine($"Package spool volume: {provider.PackageSpoolAvailableBytes:N0} bytes currently available.");
            using (var evidence = new JsonLinesFile<Discovery.ObjectEvidence>(options.OutputDirectory, "discovery/objects.jsonl.gz"))
            using (var headers = new JsonLinesFile<Discovery.ExportHeader>(options.OutputDirectory, "discovery/exports.jsonl.gz"))
            using (var progress = new ExtractionProgress(Console.Error))
            {
                discovery = AssetDiscovery.Read(provider, evidence.Write, headers.Write, (registry, files) =>
                {
                    Snapshot.WriteLines(options.OutputDirectory, "discovery/files.jsonl.gz", files);
                    Snapshot.WriteLines(options.OutputDirectory, "discovery/registry.jsonl.gz", registry);
                }, progress);
                progress.Set("finish-evidence");
                headers.Complete();
                evidence.Complete();
            }
            issues.AddRange(discovery.Issues);
            notices.AddRange(discovery.Notices);
            Snapshot.WriteLines(options.OutputDirectory, "discovery/packages.jsonl.gz", discovery.Packages);
            Snapshot.WriteLines(options.OutputDirectory, "discovery/package-index.jsonl.gz", new[] { provider.InputIndex.Record });
            var materials = new MaterialIcons(provider,
                new(TextureAddress.TA_Wrap, TextureAddress.TA_Wrap, TextureFilter.TF_Bilinear),
                new(TextureAddress.TA_Clamp, TextureAddress.TA_Clamp, TextureFilter.TF_Bilinear));
            var resources = new ImageResources(options.OutputDirectory, issues, materials);
            Console.WriteLine("Exporting registry UI textures...");
            foreach (var texture in discovery.UiTextures) resources.Export(texture, provider.Load);
            records = ExportAssets(discovery.Assets, provider, options.OutputDirectory, resources, issues, notices);
            Snapshot.Write(options.OutputDirectory, "resources.json", resources.Entries);
            resourceCount = resources.Entries.Count;
            if (records.Count == 0)
                issues.Add(new("validation", "assets", "No asset IDs were extracted."));
        }
        catch (Exception error)
        {
            issues.Add(new("fatal", "extraction", error.Message));
        }
        issues.AddRange(diagnostics.Issues.Distinct());
        notices.AddRange(diagnostics.Notices);
        var report = new RunReport(issues.Count == 0 ? "succeeded" : "incomplete",
            discovery?.RegisteredAssets ?? 0, discovery?.Candidates ?? 0, discovery?.Loaded ?? 0, records.Count,
            records.Count(asset => asset.Text.Any(text => text.Locale == "en" && text.DisplayName.Length > 0)),
            records.Count(asset => asset.Text.Any(text => text.Locale == "en" && text.Description.Length > 0)),
            records.Count(asset => asset.Images.Any(image => image.Status == "exported")), issues,
            notices.Distinct().ToArray(), new(Discovery.EvidenceReader.NativeScope,
                Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(options.Usmap))), discovery?.Objects.Count ?? 0, resourceCount,
                discovery?.UnavailableSoftReferences ?? 0, discovery?.UnavailableHardReferences ?? 0,
                discovery?.UnmappedNonCatalogExports ?? 0, inputContainers));
        Snapshot.Write(options.OutputDirectory, "assets.json", records);
        Snapshot.WriteFile(options.OutputDirectory, CatalogCsv.MainFile,
            stream => CatalogCsv.Write(stream, CatalogCsv.MainRows(records)));
        Snapshot.WriteFile(options.OutputDirectory, CatalogCsv.LocalizationFile,
            stream => CatalogCsv.Write(stream, CatalogCsv.LocalizationRows(records)));
        Snapshot.Write(options.OutputDirectory, "coverage.json", report);
        Console.WriteLine($"{report.Status}: {report.AssetIds:N0} IDs, {report.EnglishNames:N0} English names, {report.Images:N0} assets with images, {issues.Count:N0} issues.");
        return issues.Count == 0 ? 0 : 1;
    }

    private static List<AssetRecord> ExportAssets(IReadOnlyList<CatalogAsset> assets, PackageProvider provider,
        string output, ImageResources resources, List<ExtractionIssue> issues, List<ExtractionIssue> notices)
    {
        Console.WriteLine($"Found {assets.Count:N0} IDs. Reading text...");
        var texts = assets.Select(asset => Text.Read(asset, issues)).ToArray();
        notices.AddRange(texts.SelectMany(text => text.Notices));
        var textById = texts.ToDictionary(text => text.AssetId);
        var localized = Text.Localize(provider, texts, issues, (locale, entries) =>
            Snapshot.WriteLines(output, $"localization/{locale}.jsonl.gz", entries
                .OrderBy(space => space.Key, StringComparer.Ordinal)
                .SelectMany(space => space.Value.OrderBy(entry => entry.Key, StringComparer.Ordinal)
                    .Select(entry => new { Namespace = space.Key, entry.Key, Value = entry.Value })))).ToLookup(row => row.AssetId);
        Console.WriteLine("Exporting referenced images...");
        var records = new List<AssetRecord>();
        foreach (var asset in assets)
        {
            records.Add(new(asset.Id,
                asset.Definitions.Select(source => source.Reference).ToArray(),
                asset.Metadata.Select(source => source.Reference).ToArray(),
                localized[asset.Id].Select(text => new Translation(text.Locale, text.DisplayName, text.Description)).ToArray(),
                Images.Export(asset, resources, provider.Load, issues))
            {
                Presentation = new(textById[asset.Id].Name, textById[asset.Id].Description, textById[asset.Id].Candidates,
                    asset.PresentationNames.Select(name => new ContainerPresentation(name.Role, name.ContainerType,
                        name.FramePath, name.ContainerIndex, name.SlotPath, name.ContainerPath, name.Metadata.Path)).ToArray())
                {
                    VisualSlots = asset.VisualSlotNames.Select(name => new VisualSlotPresentation(name.SlotPath,
                        name.TypeTag, name.Metadata.Path, name.Members.Select(member =>
                            new VisualSlotMemberPresentation(member.ItemPath, member.MetadataPath)).ToArray())).ToArray(),
                    InventoryRoots = asset.InventoryRootNames.Select(name => new InventoryRootPresentation(name.Match.Role,
                        name.Match.ContainerType, name.Match.RootPath, name.Match.RootField, name.Match.SlotPath,
                        name.Match.ContainerPath, name.Metadata.Path)).ToArray()
                }
            });
            if (records.Count % 250 == 0)
                Console.WriteLine($"Processed {records.Count:N0}/{assets.Count:N0} assets.");
        }
        return records;
    }

    internal sealed class ParserDiagnostics : ILogEventSink
    {
        private readonly DiagnosticSamples diagnostics = new(Console.Error);
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
            {
                Issues.Enqueue(issue);
                diagnostics.Write(issue, logEvent.MessageTemplate.Text);
            }
        }
    }
}
