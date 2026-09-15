using System.Text.Json;
using static PublishSnapshot.Preview;

namespace PublishSnapshot;

internal sealed record MountedInputs(HashSet<string> Paths, HashSet<string> Unindexed)
{
    public static MountedInputs Read(SnapshotFile file, IReadOnlySet<string> registryPackages)
    {
        var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var unindexed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var mapped = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        JsonLines.Read(file, row =>
        {
            Fields(row, "path", "registryPackages");
            var path = String(row, "path");
            Require(!path.Any(char.IsControl) && paths.Add(path), "Invalid or duplicate mounted package path.");
            var packages = row.GetProperty("registryPackages");
            Require(packages.ValueKind == JsonValueKind.Array, "Mounted registry packages must be an array.");
            if (packages.GetArrayLength() == 0) unindexed.Add(path);
            foreach (var package in packages.EnumerateArray())
            {
                Require(package.ValueKind == JsonValueKind.String, "Invalid mounted registry package.");
                var name = package.GetString()!;
                Require(registryPackages.Contains(name) && mapped.Add(name), "Unknown or duplicate registry package crosswalk.");
            }
        });
        Require(paths.Count > 0, "No mounted input packages.");
        Require(mapped.SetEquals(registryPackages), "A registry package is absent from mounted inputs.");
        return new(paths, unindexed);
    }
}
