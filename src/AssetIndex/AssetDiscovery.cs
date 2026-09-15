using CUE4Parse.GameTypes.Theia.FileProvider;
using CUE4Parse.MappingsProvider;
using CUE4Parse.UE4.AssetRegistry;
using CUE4Parse.UE4.AssetRegistry.Objects;
using CUE4Parse.UE4.Assets.Exports;

namespace AssetIndex;

internal sealed record DiscoveryResult(
    IReadOnlyList<CatalogAsset> Assets,
    IReadOnlyList<ExtractionIssue> Issues,
    int RegisteredAssets,
    int Candidates,
    int Loaded);

internal static class AssetDiscovery
{
    public static DiscoveryResult Read(TheiaFileProvider provider)
    {
        var issues = new List<ExtractionIssue>();
        var registry = ReadRegistry(provider, issues);
        var mappings = provider.MappingsForGame
            ?? throw new InvalidDataException("Asset discovery requires type mappings.");
        var candidates = CandidatePackages(registry, mappings, issues);
        var objects = new List<UObject>();
        var loaded = 0;
        var attempted = 0;

        foreach (var path in candidates)
        {
            try
            {
                var package = provider.LoadPackage(path);
                foreach (var export in package.ExportsLazy)
                {
                    try
                    {
                        objects.Add(export.Value);
                    }
                    catch (Exception exception)
                    {
                        issues.Add(new("decode", path, DescribeError(exception)));
                    }
                }

                loaded++;
            }
            catch (Exception exception)
            {
                issues.Add(new("package", path, DescribeError(exception)));
            }

            attempted++;
            if (attempted % 100 == 0 || attempted == candidates.Count)
                Console.Error.WriteLine($"Read {attempted}/{candidates.Count} candidate packages; {loaded} loaded, {issues.Count} issues.");
        }

        ReportUnindexedPackages(provider, registry, issues);
        var assets = Assets.Collect(objects, mappings, issues);
        return new(assets, issues, registry.Count, candidates.Count, loaded);
    }

    internal static string DescribeError(Exception error)
    {
        var cause = error.GetBaseException().Message;
        return cause == error.Message ? error.Message : $"{error.Message} Cause: {cause}";
    }

    private static IReadOnlyList<FAssetData> ReadRegistry(TheiaFileProvider provider, List<ExtractionIssue> issues)
    {
        var assets = new Dictionary<string, FAssetData>(StringComparer.Ordinal);
        var registries = provider.Files.Values
            .Where(file => Path.GetFileName(file.Path).Equals("AssetRegistry.bin", StringComparison.OrdinalIgnoreCase))
            .OrderBy(file => file.Path, StringComparer.Ordinal);
        foreach (var file in registries)
        {
            try
            {
                using var reader = file.CreateReader();
                var registry = new FAssetRegistryState(reader);
                foreach (var asset in registry.PreallocatedAssetDataBuffers)
                    assets.TryAdd(asset.ObjectPath, asset);
            }
            catch (Exception exception)
            {
                issues.Add(new("registry", file.Path, DescribeError(exception)));
            }
        }

        if (assets.Count == 0)
            issues.Add(new("registry", "AssetRegistry.bin", "No registered assets were read."));
        return assets.Values.OrderBy(asset => asset.ObjectPath, StringComparer.Ordinal).ToArray();
    }

    private static IReadOnlyList<string> CandidatePackages(
        IReadOnlyList<FAssetData> registry, TypeMappings mappings, List<ExtractionIssue> issues)
    {
        var packages = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var group in registry.GroupBy(asset => asset.AssetClass.Text))
        {
            var candidate = Assets.IsA(mappings, group.Key, "DataAsset");
            var metadata = Assets.IsA(mappings, group.Key, "UIMetaDataItem");
            if (candidate == false && metadata == false)
                continue;
            if (candidate is null && metadata != true)
            {
                issues.Add(new("schema", group.Key, $"Class ancestry is unknown for {group.Count()} registered assets; skipped until mappings identify their class."));
                continue;
            }
            foreach (var asset in group)
                packages.Add(asset.PackageName.Text);
        }

        return packages.ToArray();
    }

    private static void ReportUnindexedPackages(
        TheiaFileProvider provider, IReadOnlyList<FAssetData> registry, List<ExtractionIssue> issues)
    {
        var indexed = registry.Select(asset => Path.ChangeExtension(provider.FixPath(asset.PackageName.Text), null))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var unindexed = provider.Files.Values
            .Where(file => file.IsUePackage && !indexed.Contains(Path.ChangeExtension(provider.FixPath(file.Path), null)))
            .Select(file => file.Path).Order(StringComparer.Ordinal).ToArray();
        if (unindexed.Length > 0)
            issues.Add(new("coverage", "AssetRegistry.bin", $"{unindexed.Length} packages are absent from the registry; their classes and IDs remain unaudited. Examples: {string.Join(", ", unindexed.Take(5))}"));
    }
}
