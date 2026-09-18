using System.Globalization;
using System.Text.Json;
using static PublishSnapshot.Preview;

namespace PublishSnapshot;

// Replay identity contracts from selected decoded properties. A reward, name,
// filename or unrelated numeric field never supplies an asset identity.
internal sealed class IdentityContext(DecodedEvidence evidence, IdentitySchemas schemas)
{
    private static readonly (string Class, string Field)[] MetadataLinks =
    [
        ("UIPlayerStatsRaiderTargetMetaDataItem", "PlayerStatsRaiderTargetDataAsset"),
        ("UIInteractQuestMetaDataItem", "InteractQuestDataAsset"),
        ("UIQuestAreaMetaDataItem", "WorldQuestDataAsset"),
        ("UIXPEventCategoryMetaDataItem", "XPEventCategoryDataAsset"),
        ("UIQuestObjectiveParameterMetaDataItem", "Asset")
    ];
    internal static readonly string[] RootFields =
    [
        "AssetId", "bOverrideItemAssetId", "OverrideItemAssetId", "bOverrideAssetId", "OverrideAssetId",
        "PersistenceDataAsset", "PlayerStatsRaiderTargetDataAsset", "InteractQuestDataAsset",
        "WorldQuestDataAsset", "XPEventCategoryDataAsset", "Asset"
    ];

    public IdentitySchema Schema(string path) => schemas.Schema(path);
    public IdentityProperty? StructProperty(string structure, string name, params string[] types) => schemas.StructProperty(structure, name, types);
    public bool IsClassDefault(string path) => schemas.IsClassDefault(path);

    public void Validate(JsonElement assets)
    {
        var declared = new Dictionary<string, (long Id, bool Metadata)>(StringComparer.OrdinalIgnoreCase);
        foreach (var asset in assets.EnumerateArray())
        {
            var expected = Id(String(asset, "id"));
            var definitions = asset.GetProperty("definitions").EnumerateArray()
                .Select(source => ResourceEvidence.ObjectPath(source, "path")).ToHashSet(StringComparer.OrdinalIgnoreCase);
            Require(definitions.Count > 0, "Identity proof requires a definition.");
            foreach (var source in asset.GetProperty("definitions").EnumerateArray()) ValidateSource(source, false);
            foreach (var source in asset.GetProperty("metadata").EnumerateArray()) ValidateSource(source, true);

            void ValidateSource(JsonElement source, bool metadata)
            {
                var path = ResourceEvidence.ObjectPath(source, "path");
                Require(!IsClassDefault(path), "A class default object is not a catalog definition or metadata item.");
                var schema = Schema(path);
                var decodedPath = evidence.Object(path).Path;
                var nameStart = Math.Max(decodedPath.LastIndexOf('.'), decodedPath.LastIndexOf(':')) + 1;
                Require(String(source, "name") == decodedPath[nameStart..], "Catalog source name differs from its decoded object name.");
                Require(String(source, "class").Equals(schema.SourceClass, StringComparison.OrdinalIgnoreCase),
                    "Catalog source class differs from decoded identity evidence.");
                Require(schema.IsA("UIMetaDataItem") == metadata, "Catalog source has the wrong identity role.");
                var actual = metadata ? MetadataId(path, definitions) : DefinitionId(path, definitions);
                Require(actual == expected, $"Catalog ID differs from typed identity evidence at {path}.");
                Require(declared.TryAdd(path, (expected, metadata)), $"Catalog repeats source identity: {path}.");
            }
        }
        ValidateCompleteness(declared);
    }

    private void ValidateCompleteness(IReadOnlyDictionary<string, (long Id, bool Metadata)> declared)
    {
        foreach (var source in evidence.Objects)
        {
            if (IsClassDefault(source.Path)) continue;
            var schema = Schema(source.Path);
            var metadata = schema.IsA("UIMetaDataItem");
            var id = metadata ? MetadataId(source.Path) : DefinitionId(source.Path);
            if (id is null) continue;
            Require(declared.TryGetValue(source.Path, out var actual) && actual == (id.Value, metadata),
                $"Catalog omits decoded {(metadata ? "metadata" : "definition")} identity {id}: {source.Path}.");
        }
    }

    public long? DefinitionId(string path) => DefinitionId(path, null);
    public long? MetadataId(string path) => MetadataId(path, null);

    private long? DefinitionId(string path, IReadOnlySet<string>? definitions)
    {
        var schema = Schema(path);
        if (schema.IsA("PersistenceDataAsset") || schema.IsA("OptionalPersistenceDataAsset")) return Integer(path, "AssetId");
        if (schema.IsA("ItemDataAssetBase") && Override(path, "bOverrideItemAssetId", "OverrideItemAssetId") is { } overridden)
            return overridden;
        if (!schema.IsA("DataAsset") || schema.Property("PersistenceDataAsset", "ObjectProperty", "SoftObjectProperty") is null) return null;
        var persistence = Reference(path, "PersistenceDataAsset");
        if (persistence is null) return null;
        Require(definitions is null || definitions.Contains(persistence), "Identity persistence target is absent from catalog definitions.");
        var target = Schema(persistence);
        Require(target.IsA("PersistenceDataAsset") || target.IsA("OptionalPersistenceDataAsset"), "Identity target is not a native persistence class.");
        return Integer(persistence, "AssetId");
    }

    private long? MetadataId(string path, IReadOnlySet<string>? definitions)
    {
        var schema = Schema(path);
        Require(schema.IsA("UIMetaDataItem"), "Metadata identity requires a native metadata class.");
        var links = MetadataLinks.Where(link => schema.IsA(link.Class)).ToArray();
        Require(links.Length <= 1, "Metadata has conflicting native identity contracts.");
        var field = links.Length == 1 ? links[0].Field : "PersistenceDataAsset";
        var hasReference = schema.Property(field, "ObjectProperty", "SoftObjectProperty") is not null;
        var hasOverride = schema.Property("OverrideAssetId", "Int64Property") is not null;
        if (!hasReference && !hasOverride) return null;
        if (Override(path, "bOverrideAssetId", "OverrideAssetId") is { } overridden) return overridden;
        var target = hasReference ? Reference(path, field) : null;
        Require(target is not null, $"Metadata has no resolvable typed identity: {path}.");
        Require(definitions is null || definitions.Contains(target!), "Metadata identity target is absent from catalog definitions.");
        Require(!Schema(target!).IsA("UIMetaDataItem"), "Metadata identity target cannot be metadata.");
        var id = DefinitionId(target!, definitions);
        Require(id is not null, $"Metadata has no resolvable typed identity: {path}.");
        return id;
    }

    private long? Override(string path, string enabled, string value)
    {
        if (Schema(path).Property(enabled, "BoolProperty") is null) return null;
        var flag = Field(path, enabled, "BoolProperty");
        if (flag is null) return null; // Complete template chain proves native zero initialization.
        var scalar = flag.Value("boolean");
        Require(scalar.Type == "BoolProperty" && scalar.Value is "true" or "false", "Malformed identity override flag.");
        return scalar.Value == "true" ? Integer(path, value) : null;
    }

    private long Integer(string path, string name)
    {
        var field = Field(path, name, "Int64Property");
        Require(field is not null, $"Missing typed identity integer: {path}.{name}.");
        var scalar = field!.Value("integer");
        Require(scalar.Type == "Int64Property" && scalar.Value is not null, "Malformed identity integer.");
        return Id(scalar.Value!);
    }

    private string? Reference(string path, string name) => Field(path, name, "ObjectProperty", "SoftObjectProperty")?.Reference();

    // Template bodies can own a value, but an unrelated object's same-named field
    // cannot. Check the entire traversed class chain before accepting inheritance.
    public EvidenceField? Field(string path, string name, params string[] types)
    {
        var declaration = Schema(path).Property(name, types);
        Require(declaration is not null, $"Missing identity declaration: {path}.{name}.");
        var field = FindField(path, name);
        if (field is null) return null;
        var owner = Schema(field.Owner.Path).Property(name, types);
        Require(owner is not null && owner.Type == declaration!.Type && field.Header.Type == declaration.Type,
            $"Identity property differs from its declaration: {field.Owner.Path}.{name}.");
        return field;
    }

    public EvidenceField? FindField(string path, string name)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (string? current = path; current is not null;)
        {
            Require(seen.Add(current) && seen.Count <= 128, "Identity template chain repeats or exceeds 128 levels.");
            var schema = Schema(current);
            var source = evidence.Object(current);
            if (source.Field(name) is { } field) return field;
            var next = source.Link("/Template");
            if (next is not null)
            {
                var template = Schema(next);
                Require(schema.QualifiedAncestry.Contains(template.ClassPath, StringComparer.OrdinalIgnoreCase) ||
                    template.ClassPath.StartsWith("/Script/", StringComparison.OrdinalIgnoreCase) &&
                    schema.NativeAncestry.Contains(template.SourceClass, StringComparer.OrdinalIgnoreCase),
                    "Identity template class is outside the source class ancestry.");
            }
            current = next;
        }
        return null;
    }

    private static long Id(string value)
    {
        Require(long.TryParse(value, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var id) && id != 0 &&
            id.ToString(CultureInfo.InvariantCulture) == value, "Identity must be a canonical nonzero signed 64-bit integer.");
        return id;
    }
}
