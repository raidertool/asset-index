using System.Globalization;
using System.Text.Json;
using AssetIndex;
using static PublishSnapshot.Preview;
using static PublishSnapshot.ResourceEvidence;
using static PublishSnapshot.PresentationQueryEvidence;

namespace PublishSnapshot;

internal sealed partial class PresentationRelationChecks
{
    private sealed record RelationIdentity(long Id, string Family, string Owner, string Position,
        string Role, string Target, string Metadata);
    private sealed record Target(long Id, string Role, string Path);
    private sealed record TaggedContainer(long Id, string Path, string[] Tags);

    // A supported relationship remains evidence even when a stronger direct label
    // wins. Reconstruct its membership from objects independently of output rows.
    private void ValidateCompleteness(JsonElement assets)
    {
        var actual = new HashSet<RelationIdentity>(RelationComparer.Instance);
        foreach (var asset in assets.EnumerateArray())
        {
            var id = long.Parse(String(asset, "id"), CultureInfo.InvariantCulture);
            foreach (var family in new[] { "containers", "inventoryRoots", "visualSlots" })
                foreach (var row in asset.GetProperty("presentation").GetProperty(family).EnumerateArray())
                    Require(actual.Add(Relation(id, family, row)), "Duplicate presentation relationship.");
        }
        var labels = ContainerLabels();
        var expected = ExpectedContainers(labels).Concat(ExpectedRoots(labels)).Concat(ExpectedVisualSlots())
            .ToHashSet(RelationComparer.Instance);
        foreach (var relation in expected)
            Require(actual.Remove(relation), $"Catalog omits {relation.Family} relationship for {relation.Id}: {relation.Owner} -> {relation.Target} ({relation.Metadata}).");
        Require(actual.Count == 0, "Catalog contains a presentation relationship outside the decoded selection policy.");
    }

    private static RelationIdentity Relation(long id, string family, JsonElement row)
    {
        var slot = ObjectPath(row, "slotPath");
        var metadata = ObjectPath(row, "metadataPath");
        if (family == "visualSlots") return new(id, family, slot, "", "", slot, metadata);
        var role = String(row, "role");
        var target = role == "container-slot" ? slot : ObjectPath(row, "containerPath");
        return family == "containers"
            ? new(id, family, ObjectPath(row, "framePath"), row.GetProperty("containerIndex").GetInt32().ToString(CultureInfo.InvariantCulture), role, target, metadata)
            : new(id, family, ObjectPath(row, "rootPath"), String(row, "rootField"), role, target, metadata);
    }

    private IEnumerable<DecodedObject> Instances(string type) => evidence.Objects
        .Where(source => !identities.IsClassDefault(source.Path) && identities.Schema(source.Path).IsA(type));

    private Dictionary<string, List<string>> ContainerLabels()
    {
        var labels = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var source in Instances("UIInventoryContainerMetaDataItem"))
        {
            var schema = identities.Schema(source.Path);
            if (schema.Property("ContainerType", "EnumProperty", "ByteProperty") is null) continue;
            var field = OptionalField(source.Path, "ContainerType", "EnumProperty", "ByteProperty");
            if (field is null) continue;
            Require(schema.Property("ContainerName", "TextProperty") is not null, "Container metadata has no typed ContainerName declaration.");
            var category = Enum(field);
            if (!labels.TryGetValue(category, out var group)) labels[category] = group = [];
            group.Add(source.Path);
        }
        return labels;
    }

    private static IReadOnlyList<string> Labels(IReadOnlyDictionary<string, List<string>> labels, string category)
    {
        Require(labels.TryGetValue(category, out var matches) && matches.Count > 0, $"No decoded container metadata for {category}.");
        return matches!;
    }

    private IEnumerable<Target> SlotTargets(string slot)
    {
        Instance(slot, "InventoryContainerSlotDataAsset");
        yield return new(RequiredId(slot), "container-slot", slot);
        if (Reference(slot, "DefaultContainer") is not { } container) yield break;
        Instance(container, "InventoryContainerItemDataAsset");
        yield return new(RequiredId(container), "default-container", container);
    }

    private IEnumerable<RelationIdentity> ExpectedContainers(IReadOnlyDictionary<string, List<string>> labels)
    {
        foreach (var frame in Instances("LoadoutFrameItemDataAsset"))
        {
            if (identities.Schema(frame.Path).Property("Containers", "ArrayProperty") is null) continue;
            if (OptionalField(frame.Path, "Containers", "ArrayProperty") is not { } entries) continue;
            foreach (var index in Elements(entries))
            {
                var (category, slot) = FrameEntry(frame.Path, index);
                foreach (var target in SlotTargets(slot))
                    foreach (var label in Labels(labels, category))
                        yield return new(target.Id, "containers", frame.Path, index.ToString(CultureInfo.InvariantCulture), target.Role, target.Path, label);
            }
        }
    }

    private IEnumerable<RelationIdentity> ExpectedRoots(IReadOnlyDictionary<string, List<string>> labels)
    {
        IReadOnlyList<TaggedContainer>? items = null;
        foreach (var root in Instances("InventoryTreeRootAsset"))
            foreach (var (field, category) in InventoryRootPolicy.Categories)
            {
                if (Reference(root.Path, field) is not { } slot) continue;
                var targets = SlotTargets(slot).ToList();
                var defaultContainer = targets.SingleOrDefault(target => target.Role == "default-container");
                if (defaultContainer is not null) Tags(Structure(defaultContainer.Path, "Tags", "GameplayTagContainer"));
                if (Structure(slot, "AllowedContainersQuery", "GameplayTagQuery") is { } fieldQuery)
                {
                    var query = Query(fieldQuery);
                    items ??= RootContainers();
                    targets.AddRange(items.Where(item => !Equal(item.Path, defaultContainer?.Path) && GameplayTagQueryMatch.Matches(query, item.Tags))
                        .Select(item => new Target(item.Id, "allowed-container", item.Path)));
                }
                foreach (var target in targets)
                    foreach (var label in Labels(labels, category))
                        yield return new(target.Id, "inventoryRoots", root.Path, field, target.Role, target.Path, label);
            }
    }

    private IReadOnlyList<TaggedContainer> RootContainers()
    {
        var items = new List<TaggedContainer>();
        foreach (var source in Instances("InventoryContainerItemDataAsset"))
            if (identities.DefinitionId(source.Path) is { } id)
                items.Add(new(id, source.Path, Tags(Structure(source.Path, "Tags", "GameplayTagContainer"))));
        return items;
    }

    private IEnumerable<RelationIdentity> ExpectedVisualSlots()
    {
        foreach (var slot in Instances("CharacterVisualSlotOnlineItemDataAsset"))
        {
            var id = RequiredId(slot.Path);
            var members = VisualMembers(Query(Field(slot.Path, "ItemsQuery", "StructProperty")));
            if (members.Count == 0) continue;
            var types = members.Values.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
            Require(types.Length == 1, $"Matching visual skins have different UI types: {slot.Path}.");
            var labels = Instances("UICharacterCustomizationQuickNavTabMetaDataItem")
                .Where(label => Equal(Tag(Field(label.Path, "CharacterCustomizationTypeTag", "StructProperty")), types[0])).ToArray();
            foreach (var label in labels)
            {
                Field(label.Path, "DisplayName", "TextProperty");
                yield return new(id, "visualSlots", slot.Path, "", "", slot.Path, label.Path);
            }
        }
    }

    private sealed class RelationComparer : IEqualityComparer<RelationIdentity>
    {
        public static readonly RelationComparer Instance = new();
        public bool Equals(RelationIdentity? left, RelationIdentity? right) => left is not null && right is not null &&
            left.Id == right.Id && left.Family == right.Family && Equal(left.Owner, right.Owner) && Equal(left.Position, right.Position) &&
            Equal(left.Role, right.Role) && Equal(left.Target, right.Target) && Equal(left.Metadata, right.Metadata);
        public int GetHashCode(RelationIdentity value) => HashCode.Combine(value.Id, value.Family,
            StringComparer.OrdinalIgnoreCase.GetHashCode(value.Owner), StringComparer.OrdinalIgnoreCase.GetHashCode(value.Position),
            StringComparer.OrdinalIgnoreCase.GetHashCode(value.Role), StringComparer.OrdinalIgnoreCase.GetHashCode(value.Target),
            StringComparer.OrdinalIgnoreCase.GetHashCode(value.Metadata));
    }
}
