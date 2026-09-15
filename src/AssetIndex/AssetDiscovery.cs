using AssetIndex.Discovery;
using System.Diagnostics;
using CUE4Parse.GameTypes.Theia.FileProvider;
using CUE4Parse.UE4.Assets;
using CUE4Parse.UE4.Assets.Exports;

namespace AssetIndex;

internal sealed record PackageRead(string Path, string? Name, string Reason, string Status, int Exports,
    IReadOnlyList<int> Selected, IReadOnlyList<int> Decoded);
internal sealed record DiscoveryResult(IReadOnlyList<CatalogAsset> Assets, IReadOnlyList<UObject> Objects,
    IReadOnlyList<RegisteredObject> Registry, IReadOnlyList<PackageFile> Files, IReadOnlyList<PackageRead> Packages,
    IReadOnlyList<ExtractionIssue> Issues)
{
    public int RegisteredAssets => Registry.Count;
    public int Candidates => Packages.Count;
    public int Loaded => Packages.Count(package => package.Status == "succeeded");
}

internal static class AssetDiscovery
{
    public static DiscoveryResult Read(TheiaFileProvider provider, Action<ObjectEvidence> writeEvidence, Action<ExportHeader> writeHeader,
        Action<IReadOnlyList<RegisteredObject>, IReadOnlyList<PackageFile>> writeInventory, ExtractionProgress progress) =>
        new ObjectCrawler(provider, writeEvidence, writeHeader, progress).Read(writeInventory);

    internal static string DescribeError(Exception error)
    {
        var cause = error.GetBaseException().Message;
        return cause == error.Message ? error.Message : $"{error.Message} Cause: {cause}";
    }
}

internal sealed class ObjectCrawler(TheiaFileProvider provider, Action<ObjectEvidence> writeEvidence, Action<ExportHeader> writeHeader,
    ExtractionProgress? progress = null)
{
    private readonly CUE4Parse.MappingsProvider.TypeMappings mappings = provider.MappingsForGame
        ?? throw new InvalidDataException("Asset discovery requires type mappings.");
    private readonly List<ExtractionIssue> issues = [];
    private readonly DiagnosticSamples diagnostics = new(Console.Error);
    private readonly Queue<PackageWork> pendingPackages = new();
    private readonly Queue<(PackageWork Package, ExportHeader Header)> pendingExports = new();
    private readonly Dictionary<string, PackageWork> packages = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, PackageWork> packageNames = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, (UObject Source, string Origin)> objects = new(StringComparer.OrdinalIgnoreCase);
    private readonly ReferenceClosure references = new();
    private Dictionary<string, RegisteredObject[]> registeredPackages = new(StringComparer.OrdinalIgnoreCase);
    private int inspected;
    private int decoded;

    public DiscoveryResult Read(Action<IReadOnlyList<RegisteredObject>, IReadOnlyList<PackageFile>> writeInventory)
    {
        progress?.Set("read-registry");
        var registry = Registry.Read(provider, issues);
        return Read(registry, writeInventory);
    }

    internal DiscoveryResult Read(IReadOnlyList<RegisteredObject> registry,
        Action<IReadOnlyList<RegisteredObject>, IReadOnlyList<PackageFile>> writeInventory)
    {
        progress?.Set("inventory");
        var files = PackageInventory.Read(provider, registry, issues);
        Console.Error.WriteLine($"Inventoried {files.Count:N0} effective packages; {files.Sum(file => provider[file.Path].Size):N0} package bytes before decoding.");
        progress?.Set("write-inventory");
        writeInventory(registry, files);
        progress?.Set("select-packages");
        registeredPackages = registry.GroupBy(asset => GameFiles.ResolvePackagePath(provider, asset.Package), StringComparer.OrdinalIgnoreCase).ToDictionary(group => group.Key,
            group => group.ToArray(), StringComparer.OrdinalIgnoreCase);
        foreach (var asset in registry)
            if (Registry.SelectClass(mappings, asset.Class) || Registry.IsUiTexture(asset))
                RequestPackage(asset.Package, Registry.IsUiTexture(asset) ? "ui-texture" : "definition");
        foreach (var file in files)
            RequestPackage(file.Path, file.RegistryPackages.Count == 0 ? "unindexed" : "inventory");

        while (pendingPackages.Count > 0 || pendingExports.Count > 0)
        {
            if (pendingPackages.TryDequeue(out var package))
            {
                Inspect(package);
                if (++inspected % 250 == 0) ReportProgress(package.Path);
                continue;
            }
            var (owner, header) = pendingExports.Dequeue();
            ReadExport(owner, header);
            if (++decoded % 250 == 0) ReportProgress(owner.Path);
        }
        progress?.Set("reference-closure");
        foreach (var issue in references.Check(objects.Keys)) AddIssue(issue, "reference:missing-target");
        progress?.Set("catalog");
        var allObjects = objects.OrderBy(pair => pair.Key, StringComparer.Ordinal).Select(pair => pair.Value.Source).ToArray();
        var reads = packages.Values.OrderBy(package => package.Path, StringComparer.Ordinal).Select(package => package.Report()).ToArray();
        return new(Assets.Collect(allObjects, mappings, issues), allObjects, registry, files, reads, issues);
    }

    private PackageWork RequestPackage(string path, string reason)
    {
        var physical = GameFiles.ResolvePackagePath(provider, path);
        if (packages.TryGetValue(physical, out var existing)) return existing;
        var package = new PackageWork(physical, reason);
        packages.Add(physical, package);
        pendingPackages.Enqueue(package);
        return package;
    }

    private void Inspect(PackageWork work)
    {
        progress?.Set("load-package", work.Path);
        try
        {
            work.Package = provider.LoadPackage(work.Path);
            progress?.Set("read-export-headers", work.Path);
            work.Headers = ExportInventory.Read(work.Package, work.Path, mappings);
            if (work.Headers.Count != work.Package.ExportMapLength || work.Headers.Count != work.Package.ExportsLazy.Length)
                throw new InvalidDataException("Export header and lazy body counts disagree.");
        }
        catch (Exception error)
        {
            work.Failure = AssetDiscovery.DescribeError(error);
            AddIssue(new("package", work.Path, work.Failure), "package:" + error.GetBaseException().GetType().Name);
            return;
        }

        progress?.Set("write-export-headers", work.Path);
        foreach (var header in work.Headers) writeHeader(header);
        if (!packageNames.TryAdd(work.Package.Name, work))
        {
            var first = packageNames[work.Package.Name];
            first.Issues++;
            PackageIssue(work, "metadata", work.Path, $"Duplicate logical package name {work.Package.Name}; also supplied by {first.Path}.");
        }
        foreach (var header in work.Headers)
        {
            if (header.Error is not null)
                PackageIssue(work, "metadata", $"{work.Path}#export/{header.Index}", header.Error);
            if (Registry.SelectClass(header)) Select(work, header);
        }
        foreach (var entry in registeredPackages.GetValueOrDefault(work.Path, []))
            if (Registry.SelectClass(mappings, entry.Class) || Registry.IsUiTexture(entry)) SelectRegistered(work, entry);
        foreach (var (target, source) in work.Targets) SelectTarget(work, target, source);
    }

    private void Select(PackageWork package, ExportHeader header)
    {
        if (package.Selected.Add(header.Index)) pendingExports.Enqueue((package, header));
    }

    private void SelectTarget(PackageWork work, string target, string source)
    {
        var matches = work.Headers!.Where(header => string.Equals(header.Path, target, StringComparison.OrdinalIgnoreCase)).ToArray();
        if (matches.Length != 1)
        {
            PackageIssue(work, "reference", source, $"Expected one export header for {target}; found {matches.Length}.");
            return;
        }
        var header = matches[0];
        if (Registry.FollowClass(header)) Select(work, header);
        else references.Inventory(target);
    }

    private void SelectRegistered(PackageWork work, RegisteredObject entry)
    {
        var matches = work.Headers!.Where(header => string.Equals(header.Path, entry.Path, StringComparison.OrdinalIgnoreCase)).ToArray();
        if (matches.Length == 0 && entry.Tags.TryGetValue("GeneratedClass", out var generated))
        {
            var quote = generated.IndexOf('\'');
            var path = quote < 0 ? generated : generated.EndsWith('\'') ? generated[(quote + 1)..^1] : "";
            var declaredClass = quote < 0 ? null : generated[..quote];
            matches = work.Headers!.Where(header => string.Equals(header.Path, path, StringComparison.OrdinalIgnoreCase) &&
                header.Ancestry.Contains("BlueprintGeneratedClass", StringComparer.OrdinalIgnoreCase) &&
                (declaredClass is null || string.Equals(header.Class, declaredClass, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(header.ClassPath, declaredClass, StringComparison.OrdinalIgnoreCase))).ToArray();
        }
        if (matches.Length != 1)
        {
            PackageIssue(work, "registry", entry.Path, "Selected registry object has no unique export or explicit GeneratedClass correspondence.");
            return;
        }
        Select(work, matches[0]);
    }

    private void ReadExport(PackageWork work, ExportHeader header)
    {
        var origin = $"{work.Path}#export/{header.Index}";
        progress?.Set("decode-export", work.Path, header.Index);
        UObject source;
        try { source = work.Package!.ExportsLazy[header.Index].Value; }
        catch (Exception error)
        {
            PackageIssue(work, "decode", origin, AssetDiscovery.DescribeError(error));
            return;
        }
        progress?.Set("read-evidence", work.Path, header.Index);
        var evidence = EvidenceReader.Read(source);
        if (!string.Equals(header.Path, evidence.Path, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(header.Class, evidence.Class, StringComparison.OrdinalIgnoreCase))
            PackageIssue(work, "metadata", origin, $"Decoded object {evidence.Class} {evidence.Path} disagrees with its export header {header.Class} {header.Path}.");
        if (evidence.Path.Length > 0 && !objects.TryAdd(evidence.Path, (source, origin)))
            PackageIssue(work, "decode", origin,
                $"Duplicate object path {evidence.Path}; first decoded at {objects[evidence.Path].Origin}; repeated at {origin}.");
        ReadEvidence(evidence, work, header.Index);
        work.Decoded.Add(header.Index);
    }

    private void ReadEvidence(ObjectEvidence evidence, PackageWork owner, int exportIndex)
    {
        progress?.Set("write-evidence", owner.Path, exportIndex);
        writeEvidence(evidence);
        progress?.Set("follow-references", owner.Path, exportIndex);
        foreach (var issue in evidence.Issues)
            PackageIssue(owner, "evidence", evidence.Path + issue.Pointer, issue.Message);
        foreach (var reference in evidence.References)
        {
            if (reference.Error is not null)
                PackageIssue(owner, "reference", evidence.Path + reference.Pointer, reference.Error);
            if (reference.IsNull || reference.TargetPath is not { } target || target.StartsWith("/Script/", StringComparison.OrdinalIgnoreCase)) continue;
            var packagePath = target.Split('.', 2)[0];
            if (!packagePath.StartsWith('/')) continue;
            var work = RequestPackage(packagePath, "reference");
            if (!target.Contains('.')) continue; // Package-only outer links have no export body to select.
            references.Require(evidence.Path + reference.Pointer, target);
            if (work.Targets.TryAdd(target, evidence.Path + reference.Pointer) && work.Headers is not null && work.Failure is null)
                SelectTarget(work, target, evidence.Path + reference.Pointer);
        }
    }

    private void PackageIssue(PackageWork package, string stage, string path, string message)
    {
        package.Issues++;
        AddIssue(new(stage, path, message), stage + ":" + message);
    }

    private void ReportProgress(string path)
    {
        using var process = Process.GetCurrentProcess();
        var storage = provider is PackageProvider cache
            ? $"package bytes {cache.CachedPackageBytes / (1024 * 1024):N0} MiB cached, {cache.SpooledPackageBytes / (1024 * 1024):N0} MiB spooled; "
            : "";
        Console.Error.WriteLine($"Inspected {inspected:N0} packages; {pendingPackages.Count:N0} packages and {pendingExports.Count:N0} exports pending; " +
            $"{objects.Count:N0} objects, {issues.Count:N0} issues; memory {process.WorkingSet64 / (1024 * 1024):N0} MiB resident, " +
            $"{GC.GetTotalMemory(false) / (1024 * 1024):N0} MiB managed; {storage}last {path}.");
    }

    private void AddIssue(ExtractionIssue issue, string category)
    {
        issues.Add(issue);
        diagnostics.Write(issue, category);
    }

    private sealed class PackageWork(string path, string reason)
    {
        public string Path { get; } = path;
        public string Reason { get; } = reason;
        public IPackage? Package { get; set; }
        public IReadOnlyList<ExportHeader>? Headers { get; set; }
        public string? Failure { get; set; }
        public int Issues { get; set; }
        public HashSet<int> Selected { get; } = [];
        public HashSet<int> Decoded { get; } = [];
        public Dictionary<string, string> Targets { get; } = new(StringComparer.OrdinalIgnoreCase);

        public PackageRead Report() => new(Path, Package?.Name, Reason,
            Failure is not null ? "failed" : Issues > 0 || Selected.Count != Decoded.Count ? "incomplete" : "succeeded",
            Headers?.Count ?? 0, Selected.Order().ToArray(), Decoded.Order().ToArray());
    }
}
