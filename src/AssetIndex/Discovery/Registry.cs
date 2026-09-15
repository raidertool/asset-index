using CUE4Parse.GameTypes.Theia.FileProvider;
using CUE4Parse.MappingsProvider;
using CUE4Parse.UE4.AssetRegistry;

namespace AssetIndex.Discovery;

internal sealed record RegisteredObject(string Path, string Package, string Class,
    IReadOnlyDictionary<string, string> Tags);

internal static class Registry
{
    public static IReadOnlyList<RegisteredObject> Read(TheiaFileProvider provider, ICollection<ExtractionIssue> issues)
    {
        var objects = new SortedDictionary<string, RegisteredObject>(StringComparer.Ordinal);
        foreach (var file in provider.Files.Values
            .Where(file => Path.GetFileName(file.Path).Equals("AssetRegistry.bin", StringComparison.OrdinalIgnoreCase))
            .OrderBy(file => file.Path, StringComparer.Ordinal))
        {
            try
            {
                using var reader = file.CreateReader();
                foreach (var asset in new FAssetRegistryState(reader).PreallocatedAssetDataBuffers)
                {
                    var tags = new SortedDictionary<string, string>(StringComparer.Ordinal);
                    foreach (var (key, value) in asset.TagsAndValues) tags.Add(key.Text, value);
                    var entry = new RegisteredObject(asset.ObjectPath, asset.PackageName.Text, asset.AssetClass.Text, tags);
                    if (objects.TryGetValue(entry.Path, out var previous) &&
                        (previous.Class != entry.Class || !previous.Tags.SequenceEqual(entry.Tags)))
                        issues.Add(new("registry", entry.Path, "Conflicting registry entries."));
                    else
                        objects.TryAdd(entry.Path, entry);
                }
            }
            catch (Exception error)
            {
                issues.Add(new("registry", file.Path, AssetDiscovery.DescribeError(error)));
            }
        }
        if (objects.Count == 0) throw new InvalidDataException("No asset registry entries were read.");
        return objects.Values.ToArray();
    }

    public static bool IsUiTexture(RegisteredObject asset) =>
        asset.Class == "Texture2D" && asset.Tags.TryGetValue("LODGroup", out var group) && group == "TEXTUREGROUP_UI";

    // Binary media remain inventoried; typed references can select textures and materials.
    internal static bool SelectClass(TypeMappings mappings, string type)
    {
        string[] roots = ["DataAsset", "UIMetaDataItem", "DataTable", "CurveTable", "StringTable", "Blueprint", "BlueprintGeneratedClass"];
        var ancestry = roots.Select(root => Assets.IsA(mappings, type, root)).ToArray();
        return ancestry.Any(result => result == true) || ancestry.Any(result => result is null);
    }

    internal static bool FollowClass(TypeMappings mappings, string type) => SelectClass(mappings, type) ||
        Assets.IsA(mappings, type, "Texture") == true || Assets.IsA(mappings, type, "MaterialInterface") == true;
}
