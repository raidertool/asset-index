using CUE4Parse.MappingsProvider;
using CUE4Parse.UE4.Assets.Exports;
using CUE4Parse.UE4.Objects.GameplayTags;
using CUE4Parse.UE4.Assets.Objects;
using CUE4Parse.UE4.Objects.UObject;

namespace AssetIndex;

internal sealed record InventoryRootEvidence(string Path, string Field, string DefinedAt,
    string? DefaultContainerDefinedAt, VisualSlotQuery? Query, string? TagsDefinedAt = null,
    IReadOnlyList<string>? Tags = null);
internal sealed record InventoryRootMatch(long AssetId, string Role, string ContainerType,
    string SlotPath, string? ContainerPath, InventoryRootEvidence Root);
internal sealed record InventoryRootName(InventoryRootMatch Match, ObjectReference Metadata)
{
    public long AssetId => Match.AssetId;
    public IReadOnlyList<TextCandidate> Text { get; init; } = [];
    public IReadOnlyList<ExtractionIssue> TextIssues { get; init; } = [];
}

// The game groups these persistent root slots into Stash or Augment UI categories.
// Match typed root references and eligibility queries; never infer roles from asset names.
internal sealed class InventoryRootCollector(TypeMappings mappings, ICollection<ExtractionIssue> issues)
{
    private static readonly (string Field, string Type)[] Roles =
    [
        ("StashSlot", "Stash"), ("ExpeditionStashSlot", "Stash"),
        ("BonusStashSlot", "Stash"), ("SecretStashSlot", "Stash"), ("AugmentSlot", "Augment")
    ];
    private sealed record Item(long Id, string Path, string TagsDefinedAt, string[] Tags);
    private sealed record Slot(long Id, string Path, string Type, Item? Default, InventoryRootEvidence Root);
    private readonly List<Slot> slots = [];
    private readonly Dictionary<string, Item> items = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> unreadableItems = new(StringComparer.OrdinalIgnoreCase);

    public void Observe(UObject source, ClassSchema schema)
    {
        if (schema.IsA("InventoryTreeRootAsset")) ReadRoot(source, schema);
        if (!schema.IsA("InventoryContainerItemDataAsset")) return;
        try
        {
            // Local-only container definitions have no catalog identity to name.
            if (Assets.DefinitionId(source, mappings, out _) is not null) ReadItem(source);
        }
        catch { unreadableItems.Add(ObjectMetadata.Path(source)); throw; }
    }

    public IReadOnlyList<InventoryRootMatch> Complete()
    {
        var result = new List<InventoryRootMatch>();
        foreach (var slot in slots)
        {
            result.Add(new(slot.Id, "container-slot", slot.Type, slot.Path, null, slot.Root));
            if (slot.Default is { } item)
                result.Add(Match(slot, item, "default-container"));
            if (slot.Root.Query is not { } query) continue;
            try
            {
                if (unreadableItems.Count > 0)
                    throw new InvalidDataException("Inventory-root membership cannot exclude unreadable container items.");
                // Resolve the whole query before adding members: a failed comparison cannot leave partial labels.
                var matches = items.Values.Where(item => GameplayTagQueryMatch.Matches(query, item.Tags)).ToArray();
                result.AddRange(matches.Where(item => !item.Path.Equals(slot.Default?.Path, StringComparison.OrdinalIgnoreCase))
                    .Select(item => Match(slot, item, "allowed-container")));
            }
            catch (Exception error) { issues.Add(new("presentation", slot.Root.Path + "." + slot.Root.Field, error.Message)); }
        }
        return result.OrderBy(match => match.AssetId).ThenBy(match => match.Root.Path, StringComparer.Ordinal)
            .ThenBy(match => match.Root.Field, StringComparer.Ordinal).ThenBy(match => match.Role, StringComparer.Ordinal).ToArray();
    }

    private void ReadRoot(UObject source, ClassSchema schema)
    {
        foreach (var (field, type) in Roles)
        {
            if (!schema.HasProperty(field, "ObjectProperty", "SoftObjectProperty")) continue;
            try
            {
                var property = Properties.Find(source, field, out var definedAt);
                if (property is null) continue;
                var slot = Properties.Reference(property, ObjectMetadata.Path(source) + "." + field);
                if (slot is null) continue; // An explicit null overrides a template's root role.
                slots.Add(ReadSlot(slot, new(ObjectMetadata.Path(source), field, ObjectMetadata.Path(definedAt!), null, null), type));
            }
            catch (Exception error) { issues.Add(new("presentation", ObjectMetadata.Path(source) + "." + field, error.Message)); }
        }
    }

    private Slot ReadSlot(UObject source, InventoryRootEvidence root, string type)
    {
        RequireType(source, "InventoryContainerSlotDataAsset");
        var id = Identity(source);
        var property = Properties.Find(source, "DefaultContainer", out var defaultAt);
        var container = property is null ? null : Properties.Reference(property, ObjectMetadata.Path(source) + ".DefaultContainer");
        var item = container is null ? null : ReadItem(container);
        var query = Properties.TryGet<FStructFallback>(source, "AllowedContainersQuery", out var value, out var queryAt)
            ? GameplayTagQueryMatch.Capture(value, ObjectMetadata.Path(queryAt!)) : null;
        return new(id, ObjectMetadata.Path(source), "ENewInventoryContainerType::" + type, item,
            root with { DefaultContainerDefinedAt = defaultAt is null ? null : ObjectMetadata.Path(defaultAt), Query = query });
    }

    private Item ReadItem(UObject source)
    {
        var path = ObjectMetadata.Path(source);
        if (items.TryGetValue(path, out var existing)) return existing;
        RequireType(source, "InventoryContainerItemDataAsset");
        var found = Properties.TryGet<FGameplayTagContainer>(source, "Tags", out var tags, out var definedAt);
        if (found && tags.GameplayTags is null) throw new InvalidDataException("Container Tags have no decoded tag array.");
        var item = new Item(Identity(source), path, ObjectMetadata.Path(definedAt ?? source),
            found ? tags.GameplayTags.Select(tag => GameplayTagQueryMatch.Tag(tag.TagName.Text)).ToArray() : []);
        items.Add(path, item);
        return item;
    }

    private static InventoryRootMatch Match(Slot slot, Item item, string role) => new(item.Id, role, slot.Type,
        slot.Path, item.Path, slot.Root with { TagsDefinedAt = item.TagsDefinedAt, Tags = item.Tags });

    private long Identity(UObject source) => Assets.DefinitionId(source, mappings, out _)
        ?? throw new InvalidDataException("Inventory root presentation has no resolved game identity.");

    private void RequireType(UObject source, string expected)
    {
        if (source.Flags.HasFlag(EObjectFlags.RF_ClassDefaultObject))
            throw new InvalidDataException("Inventory root presentation cannot target a class-default object.");
        if (!ClassSchema.Read(source, mappings).IsA(expected))
            throw new InvalidDataException($"Expected {expected}, found {source.ExportType} at {ObjectMetadata.Path(source)}.");
    }
}
