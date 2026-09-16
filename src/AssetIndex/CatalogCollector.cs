using System.Runtime.CompilerServices;
using CUE4Parse.MappingsProvider;
using CUE4Parse.UE4.Assets.Exports;
using CUE4Parse.UE4.Objects.UObject;

namespace AssetIndex;

// Only detached observations survive Observe; package bodies can be released after it returns.
internal sealed class CatalogCollector(TypeMappings mappings, Func<UObject, ObjectLocation> locate,
    ICollection<ExtractionIssue> issues)
{
    private readonly Dictionary<long, Dictionary<string, CatalogSource>> definitions = [];
    private readonly Dictionary<long, Dictionary<string, CatalogSource>> metadata = [];
    private readonly ConditionalWeakTable<UObject, CatalogSource> sources = new();
    private readonly PresentationCollector presentation = new(mappings, issues);
    private readonly VisualSlotLabelCollector visualSlots = new(mappings, issues);

    public void Observe(UObject source)
    {
        if (source.Flags.HasFlag(EObjectFlags.RF_ClassDefaultObject)) return;
        try { Assets.Associate(source, mappings, Add); }
        catch (Exception error) { issues.Add(new("asset", ObjectMetadata.Path(source), error.Message)); }
        presentation.Observe(source);
        visualSlots.Observe(source);
    }

    public IReadOnlyList<CatalogAsset> Complete()
    {
        var names = presentation.Complete().ToLookup(name => name.AssetId);
        var slotNames = visualSlots.Complete().ToLookup(name => name.AssetId);
        var result = new List<CatalogAsset>();
        foreach (var id in definitions.Keys.Union(metadata.Keys).Order())
        {
            var defined = Sorted(definitions, id);
            var associated = Sorted(metadata, id);
            if (defined.Length == 0)
                issues.Add(new("asset", id.ToString(), "UI metadata has no matching asset definition; retaining its explicit ID."));
            result.Add(new(id, defined, associated)
            { PresentationNames = names[id].ToArray(), VisualSlotNames = slotNames[id].ToArray() });
        }
        return result;
    }

    private void Add(long id, UObject source, bool isMetadata)
    {
        var path = ObjectMetadata.Path(source);
        var captured = sources.GetValue(source, Capture);
        var rows = isMetadata ? metadata : definitions;
        if (!rows.TryGetValue(id, out var group)) rows[id] = group = new(StringComparer.Ordinal);
        if (group.TryAdd(path, captured)) return;
        var existing = group[path];
        if (existing.Reference != captured.Reference || !existing.Text.SequenceEqual(captured.Text) ||
            !existing.Images.SequenceEqual(captured.Images))
            issues.Add(new("asset", path, $"Conflicting source observations share asset ID {id} and object path."));
    }

    private CatalogSource Capture(UObject source) => new(
        new(source.Name, source.ExportType, ObjectMetadata.Path(source)), Text.Capture(source, mappings, issues),
        Images.Capture(source, mappings, locate, issues));

    private static CatalogSource[] Sorted(Dictionary<long, Dictionary<string, CatalogSource>> rows, long id) =>
        rows.GetValueOrDefault(id)?.Values.OrderBy(source => source.Reference.Path, StringComparer.Ordinal).ToArray() ?? [];
}
