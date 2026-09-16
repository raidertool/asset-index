using CUE4Parse.MappingsProvider;
using CUE4Parse.UE4.Assets.Exports;
using CUE4Parse.UE4.Assets.Objects;
using CUE4Parse.UE4.Objects.GameplayTags;
using CUE4Parse.UE4.Objects.UObject;

namespace AssetIndex;

internal sealed record VisualSlotMember(long AssetId, string ItemPath, string PersistencePath, string MetadataPath,
    string TypeTag, string TagsDefinedAt, IReadOnlyList<string> Tags, string TypeTagDefinedAt);
internal sealed record VisualSlotName(long AssetId, string SlotPath, string TypeTag, ObjectReference Metadata,
    IReadOnlyList<VisualSlotMember> Members, VisualSlotQuery Query, string TypeTagDefinedAt)
{
    public IReadOnlyList<TextCandidate> Text { get; init; } = [];
    public IReadOnlyList<ExtractionIssue> TextIssues { get; init; } = [];
}

internal static class VisualSlotLabels
{
    public static IReadOnlyList<VisualSlotName> Read(IEnumerable<UObject> objects, TypeMappings mappings,
        ICollection<ExtractionIssue> issues)
    {
        var collector = new VisualSlotLabelCollector(mappings, issues);
        foreach (var source in objects) collector.Observe(source);
        return collector.Complete();
    }
}

// Retain values and paths only; observation must not keep decoded package objects alive.
internal sealed class VisualSlotLabelCollector(TypeMappings mappings, ICollection<ExtractionIssue> issues)
{
    private sealed record Slot(long Id, string Path, VisualSlotQuery Query);
    private sealed record Skin(long Id, string Path, string Persistence, string TagsDefinedAt, string[] Tags);
    private sealed record SkinUi(long Id, string Persistence, ObjectReference Reference, string Type, string DefinedAt);
    private sealed record Label(ObjectReference Reference, string Type, string DefinedAt,
        IReadOnlyList<TextCandidate> Text, IReadOnlyList<ExtractionIssue> Issues);

    private readonly List<Slot> slots = [];
    private readonly List<Skin> skins = [];
    private readonly List<SkinUi> skinUi = [];
    private readonly List<Label> labels = [];
    private readonly HashSet<string> observed = new(StringComparer.Ordinal);
    private readonly List<string> unreadableSkins = [];

    public void Observe(UObject source)
    {
        if (source.Flags.HasFlag(EObjectFlags.RF_ClassDefaultObject) || !observed.Add(ObjectMetadata.Path(source))) return;
        try
        {
            var schema = ClassSchema.Read(source, mappings);
            if (schema.IsA("CharacterVisualSlotOnlineItemDataAsset")) ReadSlot(source, schema);
            if (schema.IsA("CharacterVisualSkinOnlineItemDataAsset"))
            {
                try { ReadSkin(source, schema); }
                catch { unreadableSkins.Add(ObjectMetadata.Path(source)); throw; }
            }
            if (schema.IsA("UICharacterVisualSkinMetaDataItem")) ReadSkinUi(source, schema);
            if (schema.IsA("UICharacterCustomizationQuickNavTabMetaDataItem")) ReadLabel(source, schema);
        }
        catch (Exception error) { issues.Add(new("visual-slot", ObjectMetadata.Path(source), error.Message)); }
    }

    public IReadOnlyList<VisualSlotName> Complete()
    {
        var result = new List<VisualSlotName>();
        foreach (var slot in slots.OrderBy(slot => slot.Path, StringComparer.Ordinal))
        {
            try { result.AddRange(Resolve(slot)); }
            catch (Exception error) { issues.Add(new("visual-slot", slot.Path, error.Message)); }
        }
        return result.OrderBy(name => name.AssetId).ThenBy(name => name.SlotPath, StringComparer.Ordinal)
            .ThenBy(name => name.Metadata.Path, StringComparer.Ordinal).ToArray();
    }

    private IReadOnlyList<VisualSlotName> Resolve(Slot slot)
    {
        if (unreadableSkins.Count > 0)
            throw new InvalidDataException("Visual-slot membership cannot exclude unreadable skin items.");
        var matched = skins.Where(skin => GameplayTagQueryMatch.Matches(slot.Query, skin.Tags)).ToArray();
        if (matched.Length == 0) return []; // Other visual slots select settings/parts, not skin items.
        var members = new List<VisualSlotMember>();
        foreach (var skin in matched.OrderBy(skin => skin.Path, StringComparer.Ordinal))
        {
            var metadata = skinUi.Where(ui => ui.Persistence.Equals(skin.Persistence, StringComparison.OrdinalIgnoreCase)).ToArray();
            if (metadata.Length == 0 || metadata.Any(ui => ui.Id != skin.Id))
                throw new InvalidDataException($"Matching skin item has missing or contradictory UI identity: {skin.Path}.");
            members.AddRange(metadata.OrderBy(ui => ui.Reference.Path, StringComparer.Ordinal).Select(ui =>
                new VisualSlotMember(skin.Id, skin.Path, skin.Persistence, ui.Reference.Path, ui.Type,
                    skin.TagsDefinedAt, skin.Tags, ui.DefinedAt)));
        }
        var types = members.Select(member => member.TypeTag).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        if (types.Length != 1) throw new InvalidDataException("Matching skin items have different UI type tags.");
        var navigation = labels.Where(label => label.Type.Equals(types[0], StringComparison.OrdinalIgnoreCase)).ToArray();
        if (navigation.Length == 0) throw new InvalidDataException("Visual-slot UI type has no navigation label.");
        return navigation.Select(label => new VisualSlotName(slot.Id, slot.Path, types[0], label.Reference,
            members.ToArray(), slot.Query, label.DefinedAt)
        { Text = label.Text, TextIssues = label.Issues }).ToArray();
    }

    private void ReadSlot(UObject source, ClassSchema schema)
    {
        Require(schema, "ItemsQuery", "StructProperty");
        if (!Properties.TryGet<FStructFallback>(source, "ItemsQuery", out var query, out var definedAt))
            throw new InvalidDataException("Visual-slot ItemsQuery is missing.");
        slots.Add(new(Identity(source), ObjectMetadata.Path(source),
            GameplayTagQueryMatch.Capture(query, ObjectMetadata.Path(definedAt!))));
    }

    private void ReadSkin(UObject source, ClassSchema schema)
    {
        Require(schema, "Tags", "StructProperty");
        if (!Properties.TryGet<FGameplayTagContainer>(source, "Tags", out var tags, out var definedAt) || tags.GameplayTags is null)
            throw new InvalidDataException("Skin item Tags are missing.");
        var identity = Identity(source);
        var persistence = Persistence(source, schema);
        if (identity != Identity(persistence)) throw new InvalidDataException("Skin item overrides its presentation identity.");
        skins.Add(new(identity, ObjectMetadata.Path(source), ObjectMetadata.Path(persistence), ObjectMetadata.Path(definedAt!),
            tags.GameplayTags.Select(tag => GameplayTagQueryMatch.Tag(tag.TagName.Text)).ToArray()));
    }

    private void ReadSkinUi(UObject source, ClassSchema schema)
    {
        var persistence = Persistence(source, schema);
        long? id = null;
        Assets.Associate(source, mappings, (identity, _, metadata) => { if (metadata) id = identity; });
        if (id != Identity(persistence)) throw new InvalidDataException("Skin UI overrides its persistence identity.");
        var (tag, definedAt) = ReadTag(source, schema, "TypeTag");
        skinUi.Add(new(id.Value, ObjectMetadata.Path(persistence), Reference(source), tag, definedAt));
    }

    private void ReadLabel(UObject source, ClassSchema schema)
    {
        var (tag, definedAt) = ReadTag(source, schema, "CharacterCustomizationTypeTag");
        Require(schema, "DisplayName", "TextProperty");
        var textIssues = new List<ExtractionIssue>();
        var text = Text.Capture(source, mappings, textIssues).Where(candidate => candidate.Field.Equals("DisplayName", StringComparison.OrdinalIgnoreCase))
            .Select(candidate => candidate with { SourceKind = "visual-slot" }).ToArray();
        if (text.Length == 0 && textIssues.Count == 0)
            textIssues.Add(new("text", ObjectMetadata.Path(source) + ".DisplayName", "Navigation DisplayName is missing."));
        labels.Add(new(Reference(source), tag, definedAt, text, textIssues));
    }

    private UObject Persistence(UObject source, ClassSchema schema)
    {
        Require(schema, "PersistenceDataAsset", "ObjectProperty", "SoftObjectProperty");
        return Properties.Reference(source, "PersistenceDataAsset")
            ?? throw new InvalidDataException("Visual presentation has no persistence reference.");
    }

    private long Identity(UObject source) => Assets.DefinitionId(source, mappings, out _)
        ?? throw new InvalidDataException("Visual presentation has no resolved game identity.");

    private static (string Tag, string DefinedAt) ReadTag(UObject source, ClassSchema schema, string field)
    {
        Require(schema, field, "StructProperty");
        if (!Properties.TryGet<FStructFallback>(source, field, out var value, out var definedAt))
            throw new InvalidDataException($"Visual presentation {field} is missing.");
        var tag = Properties.Get<FName>(value, "TagName", ObjectMetadata.Path(source) + "." + field);
        return (GameplayTagQueryMatch.Tag(tag.Text), ObjectMetadata.Path(definedAt!));
    }

    private static void Require(ClassSchema schema, string name, params string[] kinds)
    {
        if (!schema.HasProperty(name, kinds)) throw new InvalidDataException($"Visual presentation has no {name} declaration.");
    }

    private static ObjectReference Reference(UObject source) => new(source.Name, source.ExportType, ObjectMetadata.Path(source));
}
