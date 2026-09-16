using CUE4Parse.MappingsProvider;
using CUE4Parse.UE4.Assets.Exports;

namespace AssetIndex;

internal sealed record CatalogSource(ObjectReference Reference, IReadOnlyList<TextCandidate> Text,
    IReadOnlyList<ImageRequest> Images);

internal sealed record CatalogAsset(long Id, IReadOnlyList<CatalogSource> Definitions, IReadOnlyList<CatalogSource> Metadata)
{
    public IReadOnlyList<PresentationName> PresentationNames { get; init; } = [];
}

internal static class Assets
{
    private static readonly IReadOnlyDictionary<string, string> MetadataReferences = new Dictionary<string, string>
    {
        ["UIPlayerStatsRaiderTargetMetaDataItem"] = "PlayerStatsRaiderTargetDataAsset",
        ["UIInteractQuestMetaDataItem"] = "InteractQuestDataAsset",
        ["UIQuestAreaMetaDataItem"] = "WorldQuestDataAsset",
        ["UIXPEventCategoryMetaDataItem"] = "XPEventCategoryDataAsset",
        ["UIQuestObjectiveParameterMetaDataItem"] = "Asset"
    };

    public static IReadOnlyList<CatalogAsset> Collect(
        IEnumerable<UObject> objects, TypeMappings mappings, ICollection<ExtractionIssue> issues,
        Func<UObject, ObjectLocation>? locate = null)
    {
        var collector = new CatalogCollector(mappings, locate ?? (_ =>
            throw new InvalidDataException("Image export requires a package location resolver.")), issues);
        foreach (var source in objects) collector.Observe(source);
        return collector.Complete();
    }

    internal static void Associate(UObject source, TypeMappings mappings, Action<long, UObject, bool> add)
    {
        var schema = ClassSchema.Read(source, mappings);
        if (schema.IsA("UIMetaDataItem"))
            AssociateMetadata(source, schema, mappings, add);
        else
        {
            var id = DefinitionId(source, schema, mappings, out var persistence);
            if (id is null) return;
            add(id.Value, source, false);
            if (persistence is not null) add(id.Value, persistence, false);
        }
    }

    private static void AssociateMetadata(UObject source, ClassSchema schema, TypeMappings mappings,
        Action<long, UObject, bool> add)
    {
        var referenceName = MetadataReferences.FirstOrDefault(pair =>
            schema.IsA(pair.Key)).Value ?? "PersistenceDataAsset";
        var hasReference = schema.HasProperty(referenceName, "ObjectProperty", "SoftObjectProperty");
        var hasOverride = schema.HasProperty("OverrideAssetId", "Int64Property");
        if (!hasReference && !hasOverride)
            return; // UI labels and filters without game identities are not catalog rows.

        var id = OverrideId(source, schema, "bOverrideAssetId", "OverrideAssetId");
        var target = id is null && hasReference ? Properties.Reference(source, referenceName) : null;
        UObject? persistence = null;
        id ??= target is null ? null : DefinitionId(target, mappings, out persistence);
        if (id is null)
            throw new InvalidDataException($"UI metadata has no resolvable {referenceName} or enabled asset ID override.");

        add(id.Value, source, true);
        if (target is not null) add(id.Value, target, false);
        if (persistence is not null) add(id.Value, persistence, false);
    }

    public static long? ReadId(UObject source)
    {
        if (!Properties.TryGet<long>(source, "AssetId", out var id))
            return null;
        return RequireId(id);
    }

    internal static long? DefinitionId(UObject source, TypeMappings mappings, out UObject? persistence) =>
        DefinitionId(source, ClassSchema.Read(source, mappings), mappings, out persistence);

    private static long? DefinitionId(UObject source, ClassSchema schema, TypeMappings mappings, out UObject? persistence)
    {
        persistence = null;
        if (schema.IsA("PersistenceDataAsset") || schema.IsA("OptionalPersistenceDataAsset"))
        {
            if (!schema.HasProperty("AssetId", "Int64Property")) throw new InvalidDataException("Persistence asset has no AssetId declaration.");
            return ReadId(source) ?? throw new InvalidDataException("Persistence asset has no AssetId.");
        }
        if (schema.IsA("ItemDataAssetBase"))
        {
            var overridden = OverrideId(source, schema, "bOverrideItemAssetId", "OverrideItemAssetId");
            if (overridden is not null)
                return overridden;
        }
        if (!schema.IsA("DataAsset") || !schema.HasProperty("PersistenceDataAsset", "ObjectProperty", "SoftObjectProperty"))
            return null;

        persistence = Properties.Reference(source, "PersistenceDataAsset");
        // This is the definition's identity link, not a traversal of rewards or other asset references.
        // A local-only definition may leave that optional link empty. It remains in object discovery.
        if (persistence is null) return null;
        var targetSchema = ClassSchema.Read(persistence, mappings);
        if (!(targetSchema.IsA("PersistenceDataAsset") || targetSchema.IsA("OptionalPersistenceDataAsset")) ||
            !targetSchema.HasProperty("AssetId", "Int64Property"))
            throw new InvalidDataException("Data asset has no resolvable persistence asset: its identity target is not a persistence class.");
        return ReadId(persistence)
            ?? throw new InvalidDataException("Data asset has no resolvable persistence asset or enabled asset ID override.");
    }

    private static long? OverrideId(UObject source, ClassSchema schema, string enabledName, string overrideName)
    {
        if (!schema.HasProperty(enabledName, "BoolProperty")) return null;
        if (Properties.TryGet<bool>(source, enabledName, out var enabled) && enabled)
        {
            if (!schema.HasProperty(overrideName, "Int64Property")) throw new InvalidDataException($"{overrideName} has no property declaration.");
            if (!Properties.TryGet<long>(source, overrideName, out var id))
                throw new InvalidDataException($"{enabledName} is enabled but {overrideName} is absent.");
            return RequireId(id);
        }

        return null;
    }

    public static bool? IsA(TypeMappings mappings, string typeName, string baseName)
    {
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        string? current = typeName;
        while (current is not null)
        {
            if (current.Equals(baseName, StringComparison.OrdinalIgnoreCase))
                return true;
            if (!visited.Add(current) || !mappings.Types.TryGetValue(current, out var type))
                return null;
            current = type.SuperType;
        }

        return false;
    }

    private static long RequireId(long id) => id != 0
        ? id
        : throw new InvalidDataException("Asset ID is zero; no game identity was resolved.");

}
