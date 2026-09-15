using AssetIndex.Discovery;
using System.Diagnostics;
using CUE4Parse.GameTypes.Theia.FileProvider;
using CUE4Parse.UE4.Assets.Exports;

namespace AssetIndex;

internal sealed record PackageRead(string Path, string Reason, string Status, int Exports, int Loaded);
internal sealed record DiscoveryResult(IReadOnlyList<CatalogAsset> Assets, IReadOnlyList<UObject> Objects,
    IReadOnlyList<RegisteredObject> Registry, IReadOnlyList<PackageRead> Packages,
    IReadOnlyList<ExtractionIssue> Issues)
{
    public int RegisteredAssets => Registry.Count;
    public int Candidates => Packages.Count;
    public int Loaded => Packages.Count(package => package.Status == "loaded");
}

internal static class AssetDiscovery
{
    public static DiscoveryResult Read(TheiaFileProvider provider, Action<ObjectEvidence> writeEvidence) =>
        new ObjectCrawler(provider, writeEvidence).Read();

    internal static string DescribeError(Exception error)
    {
        var cause = error.GetBaseException().Message;
        return cause == error.Message ? error.Message : $"{error.Message} Cause: {cause}";
    }
}

internal sealed class ObjectCrawler(TheiaFileProvider provider, Action<ObjectEvidence> writeEvidence)
{
    private readonly CUE4Parse.MappingsProvider.TypeMappings mappings = provider.MappingsForGame
        ?? throw new InvalidDataException("Asset discovery requires type mappings.");
    private readonly List<ExtractionIssue> issues = [];
    private readonly DiagnosticSamples diagnostics = new(Console.Error);
    private readonly SortedDictionary<string, string> pending = new(StringComparer.Ordinal);
    private readonly HashSet<string> visited = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, UObject> objects = new(StringComparer.Ordinal);
    private readonly List<PackageRead> packages = [];
    private Dictionary<string, RegisteredObject[]> registeredPackages = new(StringComparer.OrdinalIgnoreCase);

    public DiscoveryResult Read()
    {
        var registry = Registry.Read(provider, issues);
        registeredPackages = registry.GroupBy(asset => asset.Package).ToDictionary(group => group.Key,
            group => group.ToArray(), StringComparer.OrdinalIgnoreCase);
        var indexed = registry.Select(asset => Normalize(asset.Package)).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var asset in registry)
            if (Registry.SelectClass(mappings, asset.Class) || Registry.IsUiTexture(asset))
                Enqueue(asset.Package, Registry.IsUiTexture(asset) ? "ui-texture" : "definition");
        foreach (var file in provider.Files.Values.Where(file => file.IsUePackage))
            if (!indexed.Contains(Normalize(file.Path))) Enqueue(file.Path, "unindexed");

        while (pending.Count > 0)
        {
            var (path, reason) = pending.First();
            pending.Remove(path);
            if (!visited.Add(path)) continue;
            ReadPackage(path, reason);
            if (packages.Count % 250 == 0 || pending.Count == 0)
            {
                var stages = string.Join(", ", issues.GroupBy(issue => issue.Stage).OrderBy(group => group.Key)
                    .Select(group => $"{group.Key}={group.Count():N0}"));
                using var process = Process.GetCurrentProcess();
                Console.Error.WriteLine($"Read {packages.Count:N0} packages; {pending.Count:N0} pending, {objects.Count:N0} objects, {issues.Count:N0} issues ({stages}); " +
                    $"memory {process.WorkingSet64 / (1024 * 1024):N0} MiB resident, {GC.GetTotalMemory(false) / (1024 * 1024):N0} MiB managed; last {path}.");
            }
        }
        var allObjects = objects.OrderBy(pair => pair.Key, StringComparer.Ordinal).Select(pair => pair.Value).ToArray();
        return new(Assets.Collect(allObjects, mappings, issues), allObjects, registry, packages, issues);
    }

    private string Normalize(string path) => Path.ChangeExtension(provider.FixPath(GameFiles.ResolvePackagePath(provider, path)), null);

    private void Enqueue(string path, string reason)
    {
        var physical = GameFiles.ResolvePackagePath(provider, path);
        if (!visited.Contains(physical)) pending.TryAdd(physical, reason);
    }

    private void ReadPackage(string path, string reason)
    {
        CUE4Parse.UE4.Assets.IPackage package;
        try { package = provider.LoadPackage(path); }
        catch (Exception error)
        {
            AddIssue(new("package", path, AssetDiscovery.DescribeError(error)), "package:" + error.GetBaseException().GetType().Name);
            packages.Add(new(path, reason, "failed", 0, 0));
            return;
        }

        var loaded = 0;
        for (var index = 0; index < package.ExportsLazy.Length; index++)
        {
            UObject source;
            try { source = package.ExportsLazy[index].Value; }
            catch (Exception error)
            {
                AddIssue(new("decode", $"{path}#export/{index}", AssetDiscovery.DescribeError(error)), "decode:" + error.GetBaseException().GetType().Name);
                continue;
            }
            loaded++;
            var evidence = EvidenceReader.Read(source);
            if (evidence.Path.Length == 0)
            {
                ReadEvidence(evidence);
                continue;
            }
            if (objects.TryAdd(evidence.Path, source)) ReadEvidence(evidence);
        }
        packages.Add(new(path, reason, loaded == package.ExportsLazy.Length ? "loaded" : "partial", package.ExportsLazy.Length, loaded));
    }

    private void ReadEvidence(ObjectEvidence evidence)
    {
        writeEvidence(evidence);
        foreach (var issue in evidence.Issues)
            AddIssue(new("evidence", evidence.Path + issue.Pointer, issue.Message), "evidence:" + issue.Type + ":" + issue.Message);
        foreach (var reference in evidence.References)
        {
            if (reference.Error is not null)
                AddIssue(new("reference", evidence.Path + reference.Pointer, reference.Error), "reference:" + reference.Kind + ":" + reference.Error);
            if (reference.IsNull || reference.TargetPath is not { } target || target.StartsWith("/Script/", StringComparison.Ordinal)) continue;
            var packagePath = target.Split('.', 2)[0];
            if (!packagePath.StartsWith('/')) continue;
            if (registeredPackages.TryGetValue(packagePath, out var entries) &&
                !entries.Any(asset => Registry.FollowClass(mappings, asset.Class))) continue;
            Enqueue(packagePath, "reference");
        }
    }

    private void AddIssue(ExtractionIssue issue, string category)
    {
        issues.Add(issue);
        diagnostics.Write(issue, category);
    }
}
