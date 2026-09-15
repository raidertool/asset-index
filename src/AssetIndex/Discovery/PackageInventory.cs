using CUE4Parse.GameTypes.Theia.FileProvider;

namespace AssetIndex.Discovery;

internal sealed record PackageFile(string Path, IReadOnlyList<string> RegistryPackages);

internal static class PackageInventory
{
    public static IReadOnlyList<PackageFile> Read(TheiaFileProvider provider,
        IEnumerable<RegisteredObject> registry, ICollection<ExtractionIssue> issues)
    {
        // Files.Values includes older archive versions. Resolve each path through
        // normal mount precedence before inventorying the effective package inputs.
        var packages = provider.Files.Values.Where(file => file.IsUePackage)
            .Select(file => GameFiles.ResolvePackagePath(provider, file.Path))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToDictionary(path => path, _ => new List<string>(), StringComparer.OrdinalIgnoreCase);

        foreach (var package in registry.Select(asset => asset.Package).Order(StringComparer.Ordinal)
            .Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var physical = GameFiles.ResolvePackagePath(provider, package);
            if (packages.TryGetValue(physical, out var names))
                names.Add(package);
            else
                issues.Add(new("inventory", package, "Registry package does not resolve to a mounted UE package."));
        }

        return packages.OrderBy(pair => pair.Key, StringComparer.Ordinal)
            .Select(pair => new PackageFile(pair.Key, pair.Value.ToArray())).ToArray();
    }
}
