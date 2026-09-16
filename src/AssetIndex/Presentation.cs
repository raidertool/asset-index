using CUE4Parse.MappingsProvider;
using CUE4Parse.UE4.Assets.Exports;
using CUE4Parse.UE4.Assets.Objects;
using CUE4Parse.UE4.Objects.UObject;

namespace AssetIndex;

internal sealed record PresentationName(long AssetId, string Role, string ContainerType,
    string FramePath, int ContainerIndex, string SlotPath, string? ContainerPath, ObjectReference Metadata)
{
    public IReadOnlyList<TextCandidate> Text { get; init; } = [];
    public IReadOnlyList<ExtractionIssue> TextIssues { get; init; } = [];
}

internal static class Presentation
{
    public static IReadOnlyList<PresentationName> Read(IEnumerable<UObject> objects,
        TypeMappings mappings, ICollection<ExtractionIssue> issues)
    {
        var collector = new PresentationCollector(mappings, issues);
        foreach (var source in objects) collector.Observe(source);
        return collector.Complete();
    }
}

internal sealed class PresentationCollector(TypeMappings mappings, ICollection<ExtractionIssue> issues)
{
    private sealed record Label(ObjectReference Reference, IReadOnlyList<TextCandidate> Text,
        IReadOnlyList<ExtractionIssue> Issues);
    private sealed record Link(long Id, string Role, string SlotPath, string? ContainerPath);
    private sealed record Container(string FramePath, int Index, string Type, IReadOnlyList<Link> Links,
        IReadOnlyList<ExtractionIssue> Issues);

    private readonly Dictionary<string, Dictionary<string, Label>> metadata = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<Container> containers = [];
    private readonly HashSet<string> observed = new(StringComparer.Ordinal);
    private readonly InventoryRootCollector roots = new(mappings, issues);

    public void Observe(UObject source)
    {
        if (source.Flags.HasFlag(EObjectFlags.RF_ClassDefaultObject) || !observed.Add(ObjectMetadata.Path(source))) return;
        try
        {
            var schema = ClassSchema.Read(source, mappings);
            roots.Observe(source, schema);
            if (schema.IsA("LoadoutFrameItemDataAsset")) ReadFrame(source, schema);
            if (schema.IsA("UIInventoryContainerMetaDataItem") && schema.HasProperty("ContainerType", "EnumProperty", "ByteProperty") &&
                Properties.TryGet<FName>(source, "ContainerType", out var type))
            {
                if (!metadata.TryGetValue(type.Text, out var entries)) metadata[type.Text] = entries = new(StringComparer.Ordinal);
                var textIssues = new List<ExtractionIssue>();
                var text = Text.CaptureContainer(source, textIssues);
                entries.TryAdd(ObjectMetadata.Path(source), new(new(source.Name, source.ExportType, ObjectMetadata.Path(source)), text, textIssues));
            }
        }
        catch (Exception error) { issues.Add(new("presentation", ObjectMetadata.Path(source), error.Message)); }
    }

    public IReadOnlyList<PresentationName> Complete()
    {
        var names = new List<PresentationName>();
        foreach (var container in containers)
        {
            if (!metadata.TryGetValue(container.Type, out var labels))
            {
                issues.Add(new("presentation", EntryPath(container.FramePath, container.Index),
                    $"No container presentation metadata for {container.Type}."));
                continue;
            }
            foreach (var issue in container.Issues) issues.Add(issue);
            foreach (var link in container.Links)
                foreach (var label in labels.Values)
                    names.Add(new(link.Id, link.Role, container.Type, container.FramePath, container.Index,
                        link.SlotPath, link.ContainerPath, label.Reference)
                    { Text = label.Text, TextIssues = label.Issues });
        }
        return names.Distinct().OrderBy(name => name.AssetId).ThenBy(name => name.FramePath, StringComparer.Ordinal)
            .ThenBy(name => name.ContainerIndex).ThenBy(name => name.Metadata.Path, StringComparer.Ordinal).ToArray();
    }

    public IReadOnlyList<InventoryRootName> CompleteRoots()
    {
        var names = new List<InventoryRootName>();
        foreach (var match in roots.Complete())
        {
            if (!metadata.TryGetValue(match.ContainerType, out var labels))
            {
                issues.Add(new("presentation", match.Root.Path + "." + match.Root.Field,
                    $"No container presentation metadata for {match.ContainerType}."));
                continue;
            }
            names.AddRange(labels.Values.OrderBy(label => label.Reference.Path, StringComparer.Ordinal)
                .Select(label => new InventoryRootName(match, label.Reference) { Text = label.Text, TextIssues = label.Issues }));
        }
        return names;
    }

    private void ReadFrame(UObject frame, ClassSchema schema)
    {
        if (!schema.HasProperty("Containers", "ArrayProperty") ||
            !Properties.TryGet<FStructFallback[]>(frame, "Containers", out var entries)) return;
        for (var index = 0; index < entries.Length; index++)
        {
            var path = EntryPath(ObjectMetadata.Path(frame), index);
            string type;
            try { type = Properties.Get<FName>(entries[index], "Type", path).Text; }
            catch (Exception error)
            {
                issues.Add(new("presentation", path, error.Message));
                continue;
            }
            var links = new List<Link>();
            var pending = new List<ExtractionIssue>();
            try { ReadLinks(entries[index], path, links); }
            catch (Exception error) { pending.Add(new("presentation", path, error.Message)); }
            // Type labels may arrive after this frame; preserve the typed facts, never the package objects.
            containers.Add(new(ObjectMetadata.Path(frame), index, type, links, pending));
        }
    }

    private void ReadLinks(FStructFallback entry, string path, List<Link> links)
    {
        var slot = Properties.Get<FPackageIndex>(entry, "ContainerSlotDataAsset", path).Load()
            ?? throw new InvalidDataException("ContainerSlotDataAsset could not be loaded.");
        RequireType(slot, "InventoryContainerSlotDataAsset");
        var slotId = Assets.DefinitionId(slot, mappings, out _)
            ?? throw new InvalidDataException("Container slot identity could not be resolved.");
        links.Add(new(slotId, "container-slot", ObjectMetadata.Path(slot), null));

        var container = Properties.Reference(slot, "DefaultContainer");
        if (container is null) return;
        RequireType(container, "InventoryContainerItemDataAsset");
        var containerId = Assets.DefinitionId(container, mappings, out _)
            ?? throw new InvalidDataException("Default container identity could not be resolved.");
        links.Add(new(containerId, "default-container", ObjectMetadata.Path(slot), ObjectMetadata.Path(container)));
    }

    private void RequireType(UObject source, string expected)
    {
        if (!ClassSchema.Read(source, mappings).IsA(expected))
            throw new InvalidDataException($"Expected {expected}, found {source.ExportType} at {ObjectMetadata.Path(source)}.");
    }

    private static string EntryPath(string frame, int index) => $"{frame}.Containers[{index}]";
}
