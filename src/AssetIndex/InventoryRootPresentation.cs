using CUE4Parse.MappingsProvider;
using CUE4Parse.UE4.Assets.Exports;
using CUE4Parse.UE4.Assets.Objects;
using CUE4Parse.UE4.Objects.GameplayTags;
using CUE4Parse.UE4.Objects.UObject;

namespace AssetIndex;

internal sealed record InventoryRootMatch(long AssetId, string Role, string ContainerType,
    string RootPath, string RootField, string SlotPath, string? ContainerPath);
internal sealed record InventoryRootName(InventoryRootMatch Match, ObjectReference Metadata)
{
    public long AssetId => Match.AssetId;
    public IReadOnlyList<TextCandidate> Text { get; init; } = [];
    public IReadOnlyList<ExtractionIssue> TextIssues { get; init; } = [];
}

// Capture typed links while package bodies are live; retain only detached facts.
internal sealed class InventoryRootCollector(TypeMappings mappings, ICollection<ExtractionIssue> issues)
{
    private sealed record Item(long Id, string Path, string[] Tags);
    private sealed record Slot(long Id, string Path, string Category, string RootPath, string RootField,
        Item? Default, VisualSlotQuery? Query);
    private readonly List<Slot> slots = [];
    private readonly Dictionary<string, Item> items = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> unreadableItems = new(StringComparer.OrdinalIgnoreCase);

    public void Observe(UObject source, ClassSchema schema)
    {
        if (schema.IsA("InventoryTreeRootAsset")) ReadRoot(source, schema);
        if (!schema.IsA("InventoryContainerItemDataAsset")) return;
        try
        {
            // Local-only containers have no catalog identity to label.
            if (Assets.DefinitionId(source, mappings, out _) is not null) ReadItem(source);
        }
        catch { unreadableItems.Add(ObjectMetadata.Path(source)); throw; }
    }

    public IReadOnlyList<InventoryRootMatch> Complete()
    {
        var result = new List<InventoryRootMatch>();
        foreach (var slot in slots)
        {
            result.Add(Match(slot, slot.Id, "container-slot", null));
            if (slot.Default is { } item) result.Add(Match(slot, item.Id, "default-container", item.Path));
            if (slot.Query is not { } query) continue;
            try
            {
                if (unreadableItems.Count > 0)
                    throw new InvalidDataException($"Inventory-root membership cannot exclude {unreadableItems.Count} unreadable container items.");
                // Validate all comparisons before admitting any query members.
                var matches = items.Values.Where(item => GameplayTagQueryMatch.Matches(query, item.Tags)).ToArray();
                result.AddRange(matches.Where(item => !item.Path.Equals(slot.Default?.Path, StringComparison.OrdinalIgnoreCase))
                    .Select(item => Match(slot, item.Id, "allowed-container", item.Path)));
            }
            catch (Exception error) { issues.Add(new("presentation", slot.RootPath + "." + slot.RootField, error.Message)); }
        }
        return result.Distinct().OrderBy(match => match.AssetId).ThenBy(match => match.RootPath, StringComparer.Ordinal)
            .ThenBy(match => match.RootField, StringComparer.Ordinal).ThenBy(match => match.Role, StringComparer.Ordinal)
            .ThenBy(match => match.SlotPath, StringComparer.Ordinal).ThenBy(match => match.ContainerPath, StringComparer.Ordinal).ToArray();
    }

    private void ReadRoot(UObject root, ClassSchema schema)
    {
        foreach (var (field, category) in InventoryRootPolicy.Categories)
        {
            try
            {
                RequireField(schema, field, "ObjectProperty", "SoftObjectProperty");
                // Properties.Reference respects explicit null overrides in templates.
                if (Properties.Reference(root, field) is not { } slot) continue;
                var slotSchema = RequireType(slot, "InventoryContainerSlotDataAsset");
                RequireField(slotSchema, "DefaultContainer", "ObjectProperty", "SoftObjectProperty");
                RequireField(slotSchema, "AllowedContainersQuery", "StructProperty");
                var container = Properties.Reference(slot, "DefaultContainer");
                var item = container is null ? null : ReadItem(container);
                var query = Properties.TryGet<FStructFallback>(slot, "AllowedContainersQuery", out var value, out var definedAt)
                    ? GameplayTagQueryMatch.Capture(value, ObjectMetadata.Path(definedAt!)) : null;
                slots.Add(new(Identity(slot), ObjectMetadata.Path(slot), category, ObjectMetadata.Path(root), field, item, query));
            }
            catch (Exception error) { issues.Add(new("presentation", ObjectMetadata.Path(root) + "." + field, error.Message)); }
        }
    }

    private Item ReadItem(UObject source)
    {
        var path = ObjectMetadata.Path(source);
        var schema = RequireType(source, "InventoryContainerItemDataAsset");
        RequireField(schema, "Tags", "StructProperty");
        var found = Properties.TryGet<FGameplayTagContainer>(source, "Tags", out var tags);
        if (found && tags.GameplayTags is null) throw new InvalidDataException("Container Tags have no decoded tag array.");
        var item = new Item(Identity(source), path,
            found ? tags.GameplayTags.Select(tag => GameplayTagQueryMatch.Tag(tag.TagName.Text)).ToArray() : []);
        if (items.TryGetValue(path, out var existing))
        {
            if (existing.Id != item.Id || !existing.Tags.SequenceEqual(item.Tags, StringComparer.OrdinalIgnoreCase))
                throw new InvalidDataException($"Conflicting inventory-container observations for {path}.");
            return existing;
        }
        items.Add(path, item);
        return item;
    }

    private static InventoryRootMatch Match(Slot slot, long id, string role, string? container) =>
        new(id, role, slot.Category, slot.RootPath, slot.RootField, slot.Path, container);

    private long Identity(UObject source) => Assets.DefinitionId(source, mappings, out _)
        ?? throw new InvalidDataException("Inventory root presentation has no resolved game identity.");

    private ClassSchema RequireType(UObject source, string expected)
    {
        if (source.Flags.HasFlag(EObjectFlags.RF_ClassDefaultObject))
            throw new InvalidDataException("Inventory root presentation cannot target a class-default object.");
        var schema = ClassSchema.Read(source, mappings);
        if (!schema.IsA(expected))
            throw new InvalidDataException($"Expected {expected}, found {source.ExportType} at {ObjectMetadata.Path(source)}.");
        return schema;
    }

    private static void RequireField(ClassSchema schema, string name, params string[] types)
    {
        if (!schema.HasProperty(name, types))
            throw new InvalidDataException($"Inventory root presentation has no {name} property declaration.");
    }
}
