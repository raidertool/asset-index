using System.Globalization;
using System.Text.Json;
using AssetIndex;
using static PublishSnapshot.Preview;
using static PublishSnapshot.ResourceEvidence;
using static PublishSnapshot.PresentationQueryEvidence;

namespace PublishSnapshot;

// Structural checks establish ownership of declared sources. These checks prove
// the intervening game references, category fields, and complete query membership.
internal sealed class PresentationRelationChecks(DecodedEvidence evidence, IdentityContext identities)
{
    public static readonly string[] RootFields =
    [
        "Containers", "ContainerType", "DefaultContainer", "Tags", "ItemsQuery", "PersistenceDataAsset",
        "TypeTag", "CharacterCustomizationTypeTag", "AllowedContainersQuery", .. InventoryRootPolicy.Categories.Keys
    ];

    private sealed record Skin(string Path, string Persistence, long Id, string[] Tags);
    private sealed record SkinUi(string Path, string Persistence, long Id, string Type);
    private IReadOnlyList<Skin>? skins;
    private IReadOnlyList<SkinUi>? skinUi;

    public void Validate(JsonElement assets)
    {
        foreach (var asset in assets.EnumerateArray())
        {
            var id = long.Parse(String(asset, "id"), CultureInfo.InvariantCulture);
            var presentation = asset.GetProperty("presentation");
            foreach (var relation in presentation.GetProperty("containers").EnumerateArray()) Container(id, relation);
            foreach (var relation in presentation.GetProperty("visualSlots").EnumerateArray()) VisualSlot(id, relation);
            foreach (var relation in presentation.GetProperty("inventoryRoots").EnumerateArray()) InventoryRoot(id, relation);
        }
    }

    private void InventoryRoot(long id, JsonElement relation)
    {
        var root = ObjectPath(relation, "rootPath");
        var field = String(relation, "rootField");
        Require(InventoryRootPolicy.Categories.TryGetValue(field, out var category) &&
            category == String(relation, "containerType"), "Inventory root category differs from its approved field policy.");
        Instance(root, "InventoryTreeRootAsset");
        var slot = ObjectPath(relation, "slotPath");
        Same(RequiredReference(root, field), slot, "Inventory root field refers to a different slot.");
        Instance(slot, "InventoryContainerSlotDataAsset");
        RequiredId(slot);
        Category(ObjectPath(relation, "metadataPath"), category!);
        var role = String(relation, "role");
        if (role == "container-slot")
        {
            Require(relation.GetProperty("containerPath").ValueKind == JsonValueKind.Null, "Inventory root slot has a container path.");
            Identity(id, slot);
            return;
        }
        var container = ObjectPath(relation, "containerPath");
        Instance(container, "InventoryContainerItemDataAsset");
        Identity(id, container);
        var defaultContainer = Reference(slot, "DefaultContainer");
        if (role == "default-container")
            Same(defaultContainer, container, "Inventory root default reference differs from presentation.");
        else
        {
            Require(role == "allowed-container" && !Equal(defaultContainer, container), "Invalid inventory root container role.");
            var query = Query(Structure(slot, "AllowedContainersQuery", "GameplayTagQuery")
                ?? throw new InvalidDataException("Inventory root slot has no decoded allowed-container query."));
            var tags = Tags(Structure(container, "Tags", "GameplayTagContainer"));
            Require(GameplayTagQueryMatch.Matches(query, tags), "Inventory root container does not match its slot query.");
        }
    }

    private EvidenceField? Structure(string path, string field, string expected)
    {
        Require(Equal(identities.Schema(path).Property(field, "StructProperty")?.Mapping?.StructType, expected),
            "Inventory root property has the wrong mapped structure.");
        return OptionalField(path, field, "StructProperty");
    }

    private void Container(long id, JsonElement relation)
    {
        var frame = ObjectPath(relation, "framePath");
        Instance(frame, "LoadoutFrameItemDataAsset");
        var entries = Field(frame, "Containers", "ArrayProperty");
        var index = relation.GetProperty("containerIndex").GetInt32();
        Require(Elements(entries).Contains(index), "Container presentation index is not a decoded frame entry.");
        var prefix = entries.Header.Pointer + "/" + index + "/Properties";
        var typeField = entries.Owner.Field("Type", prefix)
            ?? throw new InvalidDataException("Loadout-frame entry has no Type.");
        var type = Enum(typeField);
        Require(type == String(relation, "containerType"), "Container presentation differs from its frame category.");
        var slotField = entries.Owner.Field("ContainerSlotDataAsset", prefix)
            ?? throw new InvalidDataException("Loadout-frame entry has no slot reference.");
        Type(slotField, "ObjectProperty");
        var slot = ObjectPath(relation, "slotPath");
        Same(slotField.Reference(), slot, "Loadout-frame slot reference differs from presentation.");
        Instance(slot, "InventoryContainerSlotDataAsset");
        Category(ObjectPath(relation, "metadataPath"), type);
        if (String(relation, "role") == "container-slot") Identity(id, slot);
        else
        {
            var container = ObjectPath(relation, "containerPath");
            Same(Reference(slot, "DefaultContainer"), container, "Default-container reference differs from presentation.");
            Instance(container, "InventoryContainerItemDataAsset");
            Identity(id, container);
        }
    }

    private void VisualSlot(long id, JsonElement relation)
    {
        var slot = ObjectPath(relation, "slotPath");
        Instance(slot, "CharacterVisualSlotOnlineItemDataAsset");
        Identity(id, slot);
        var query = Query(Field(slot, "ItemsQuery", "StructProperty"));
        var expected = VisualMembers(query);
        var type = String(relation, "typeTag");
        Require(expected.Count > 0 && expected.Values.All(value => Equal(value, type)),
            "Visual-slot members do not establish one declared UI type.");
        var actual = new Dictionary<(string, string), string>(MemberComparer.Instance);
        foreach (var member in relation.GetProperty("members").EnumerateArray())
            Require(actual.TryAdd((ObjectPath(member, "itemPath"), ObjectPath(member, "metadataPath")), type),
                "Duplicate visual-slot member.");
        Require(actual.Count == expected.Count && actual.Keys.All(expected.ContainsKey),
            "Visual-slot members differ from the complete game query result.");
        var metadata = ObjectPath(relation, "metadataPath");
        Instance(metadata, "UICharacterCustomizationQuickNavTabMetaDataItem");
        Require(Equal(Tag(Field(metadata, "CharacterCustomizationTypeTag", "StructProperty")), type),
            "Visual-slot navigation label has a different UI type.");
    }

    private Dictionary<(string, string), string> VisualMembers(VisualSlotQuery query)
    {
        ReadSkins();
        var result = new Dictionary<(string, string), string>(MemberComparer.Instance);
        foreach (var skin in skins!.Where(skin => GameplayTagQueryMatch.Matches(query, skin.Tags)))
        {
            var metadata = skinUi!.Where(ui => Equal(ui.Persistence, skin.Persistence)).ToArray();
            Require(metadata.Length > 0 && metadata.All(ui => ui.Id == skin.Id),
                "Matching visual skin has missing or contradictory UI identity.");
            foreach (var ui in metadata) result.Add((skin.Path, ui.Path), ui.Type);
        }
        return result;
    }

    private void ReadSkins()
    {
        if (skins is not null) return;
        var items = new List<Skin>();
        var metadata = new List<SkinUi>();
        foreach (var item in evidence.Objects)
        {
            if (identities.IsClassDefault(item.Path)) continue;
            var schema = identities.Schema(item.Path);
            if (schema.IsA("CharacterVisualSkinOnlineItemDataAsset"))
            {
                var persistence = RequiredReference(item.Path, "PersistenceDataAsset");
                var id = RequiredId(item.Path);
                Require(RequiredId(persistence) == id, "Visual skin overrides its presentation identity.");
                items.Add(new(item.Path, persistence, id, Tags(Field(item.Path, "Tags", "StructProperty"))));
            }
            if (schema.IsA("UICharacterVisualSkinMetaDataItem"))
            {
                var persistence = RequiredReference(item.Path, "PersistenceDataAsset");
                var id = identities.MetadataId(item.Path);
                Require(id is not null && id == RequiredId(persistence), "Skin UI overrides its persistence identity.");
                metadata.Add(new(item.Path, persistence, id!.Value, Tag(Field(item.Path, "TypeTag", "StructProperty"))));
            }
        }
        skins = items;
        skinUi = metadata;
    }

    private void Category(string path, string type)
    {
        Instance(path, "UIInventoryContainerMetaDataItem");
        Require(Equal(Enum(Field(path, "ContainerType", "EnumProperty", "ByteProperty")), type),
            "Container metadata names a different game category.");
    }

    private void Instance(string path, string expected)
    {
        Require(!identities.IsClassDefault(path) && identities.Schema(path).IsA(expected),
            $"Presentation source is not a {expected} instance: {path}.");
    }

    private EvidenceField Field(string path, string name, params string[] kinds) => OptionalField(path, name, kinds)
        ?? throw new InvalidDataException($"Missing presentation property {path}.{name}.");

    private EvidenceField? OptionalField(string path, string name, params string[] kinds)
    {
        var field = identities.Field(path, name, kinds);
        if (field is not null) Type(field, kinds);
        return field;
    }

    private string? Reference(string path, string name) => OptionalField(path, name, "ObjectProperty", "SoftObjectProperty")?.Reference();
    private string RequiredReference(string path, string name) => Reference(path, name)
        ?? throw new InvalidDataException($"Null presentation reference {path}.{name}.");
    private long RequiredId(string path) => identities.DefinitionId(path)
        ?? throw new InvalidDataException($"Presentation source has no game identity: {path}.");
    private void Identity(long id, string path) => Require(RequiredId(path) == id, "Presentation target has a different asset identity.");
    private static string Enum(EvidenceField field)
    {
        Type(field, "EnumProperty", "ByteProperty");
        var value = field.Value("name");
        Require(value.Type == field.Header.Type, "Presentation category has the wrong scalar type.");
        return value.Value ?? throw new InvalidDataException("Missing presentation category.");
    }
    private static bool Equal(string? left, string? right) => string.Equals(left, right, StringComparison.OrdinalIgnoreCase);
    private static void Same(string? left, string right, string message) => Require(left is not null && Equal(left, right), message);

    private sealed class MemberComparer : IEqualityComparer<(string, string)>
    {
        public static readonly MemberComparer Instance = new();
        public bool Equals((string, string) x, (string, string) y) => Equal(x.Item1, y.Item1) && Equal(x.Item2, y.Item2);
        public int GetHashCode((string, string) value) => HashCode.Combine(
            StringComparer.OrdinalIgnoreCase.GetHashCode(value.Item1), StringComparer.OrdinalIgnoreCase.GetHashCode(value.Item2));
    }
}
