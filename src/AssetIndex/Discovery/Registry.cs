using CUE4Parse.GameTypes.Theia.FileProvider;
using CUE4Parse.MappingsProvider;
using CUE4Parse.UE4.AssetRegistry;

namespace AssetIndex.Discovery;

internal sealed record RegisteredObject(string Path, string Package, string Class,
    IReadOnlyDictionary<string, string> Tags);

internal static class Registry
{
    private static readonly string[] CandidateRoots = ["DataAsset", "UIMetaDataItem", "DataTable", "CurveTable", "StringTable", "Blueprint", "BlueprintGeneratedClass"];
    private static readonly string[] ReferenceRoots = ["Texture", "MaterialInterface", "Struct", "Widget", "WidgetTree", "PanelSlot"];
    public static IReadOnlyList<RegisteredObject> Read(TheiaFileProvider provider, ICollection<ExtractionIssue> issues)
    {
        var objects = new SortedDictionary<string, RegisteredObject>(StringComparer.Ordinal);
        // Enumerating mounted files includes shadowed archive versions. Parse only
        // the current file at each registry path, using normal mount precedence.
        foreach (var path in provider.Files.Keys
            .Where(path => Path.GetFileName(path).Equals("AssetRegistry.bin", StringComparison.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase).Order(StringComparer.Ordinal))
        {
            var file = provider[path];
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

    // Binary media remain inventoried; typed references select render inputs and runtime declarations.
    internal static bool SelectClass(TypeMappings mappings, string type)
    {
        var ancestry = CandidateRoots.Select(root => Assets.IsA(mappings, type, root)).ToArray();
        return ancestry.Any(result => result == true) || ancestry.Any(result => result is null);
    }

    internal static bool FollowClass(TypeMappings mappings, string type) => SelectClass(mappings, type) ||
        ReferenceRoots.Any(root => Assets.IsA(mappings, type, root) == true);

    internal static bool SelectClass(ExportHeader header) => header.Error is not null || !header.AncestryComplete ||
        CandidateRoots.Any(root => header.Ancestry.Contains(root, StringComparer.OrdinalIgnoreCase));

    internal static bool FollowClass(ExportHeader header) => SelectClass(header) ||
        ReferenceRoots.Any(root => header.Ancestry.Contains(root, StringComparer.OrdinalIgnoreCase));
}
