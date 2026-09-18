using System.Text.Json;
using static PublishSnapshot.Preview;
using static PublishSnapshot.ResourceEvidence;

namespace PublishSnapshot;

internal static class PresentationChecks
{
    private sealed record Reference(string Namespace, string Key, string Source, bool CultureInvariant);
    private sealed record Candidate(string Kind, string Role, string Class, string Field, Reference Value);

    public static void Validate(JsonElement asset, HashSet<string> sources, Func<string, bool> objectExists)
    {
        var presentation = asset.GetProperty("presentation");
        Fields(presentation, "name", "description", "candidates", "containers", "visualSlots", "inventoryRoots");
        var containerSources = ReadContainers(presentation.GetProperty("containers"), sources, objectExists);
        var owners = new Dictionary<string, HashSet<string>>
        {
            ["definition"] = asset.GetProperty("definitions").EnumerateArray().Select(source => ObjectPath(source, "path")).ToHashSet(StringComparer.Ordinal),
            ["metadata"] = asset.GetProperty("metadata").EnumerateArray().Select(source => ObjectPath(source, "path")).ToHashSet(StringComparer.Ordinal),
            ["container"] = containerSources,
            ["visual-slot"] = VisualSlotChecks.Read(presentation.GetProperty("visualSlots"), sources, objectExists),
            ["inventory-root"] = InventoryRootChecks.Read(presentation.GetProperty("inventoryRoots"), sources, objectExists)
        };
        var candidates = new List<Candidate>();
        foreach (var candidate in presentation.GetProperty("candidates").EnumerateArray())
        {
            Fields(candidate, "role", "sourceKind", "sourcePath", "sourceClass", "field", "definedAt", "reference");
            var kind = String(candidate, "sourceKind");
            Require(owners.ContainsKey(kind), "Unknown text source kind.");
            var path = ObjectPath(candidate, "sourcePath");
            Require(owners[kind].Contains(path), "Text candidate is not linked to this asset with its declared source kind.");
            Require(objectExists(ObjectPath(candidate, "definedAt")), "Text candidate defining object is missing.");
            candidates.Add(new(kind, String(candidate, "role"), String(candidate, "sourceClass"),
                String(candidate, "field"), ReadReference(candidate.GetProperty("reference"))));
        }
        var description = Select(candidates, ["description", "tooltip"]);
        foreach (var field in new[] { "name", "description" })
        {
            var value = presentation.GetProperty(field);
            var selected = value.ValueKind == JsonValueKind.Null ? null : ReadReference(value);
            var expected = field == "name" ? SelectName(asset, candidates, description) : description;
            Require(selected == expected, $"Selected {field} does not match candidate precedence.");
        }
    }

    private static Reference? SelectName(JsonElement asset, IReadOnlyList<Candidate> candidates, Reference? description)
    {
        var definitions = asset.GetProperty("definitions").EnumerateArray()
            .Select(source => String(source, "class")).ToHashSet(StringComparer.Ordinal);
        if (definitions.Contains("NPCItemDataAsset"))
        {
            var npc = candidates.Where(candidate => candidate.Kind == "metadata" &&
                candidate.Class == "UINPCMetaDataItem" && candidate.Field == "DisplayName" &&
                candidate.Role == "display-name").ToArray();
            if (npc.Length > 0) return Select(npc, ["display-name"]);
        }
        string[] roles = ["display-name", "title", "short-name"];
        if (candidates.Any(candidate => roles.Contains(candidate.Role))) return Select(candidates, roles);
        if (definitions.Contains("SessionModifierDataAsset") &&
            candidates.Any(candidate => candidate.Role == "description" && candidate.Field == "Description" &&
                candidate.Value == description &&
                ((candidate.Kind == "definition" && candidate.Class == "SessionModifierDataAsset") ||
                 (candidate.Kind == "metadata" && candidate.Class == "UISessionModifierMetaDataItem")))) return description;
        return null;
    }

    private static Reference? Select(IReadOnlyList<Candidate> candidates, string[] roles)
    {
        foreach (var kind in new[] { "metadata", "definition", "container", "visual-slot", "inventory-root" })
            foreach (var role in roles)
            {
                var references = candidates.Where(candidate => candidate.Kind == kind && candidate.Role == role)
                    .Select(candidate => candidate.Value).Distinct().ToArray();
                if (references.Length == 0) continue;
                // The first populated tier owns the result, including empty or
                // conflicting values. Lower tiers cannot replace that observation.
                return references.Length == 1 && (references[0].Key.Length > 0 || references[0].Source.Length > 0)
                    ? references[0] : null;
            }
        return null;
    }

    private static HashSet<string> ReadContainers(JsonElement containers, HashSet<string> sources, Func<string, bool> objectExists)
    {
        var containerSources = new HashSet<string>(StringComparer.Ordinal);
        foreach (var container in containers.EnumerateArray())
        {
            Fields(container, "role", "containerType", "framePath", "containerIndex", "slotPath", "containerPath", "metadataPath");
            var role = String(container, "role");
            Require(role is "container-slot" or "default-container", "Unknown container presentation role.");
            String(container, "containerType");
            Require(container.GetProperty("containerIndex").GetInt32() >= 0, "Invalid container index.");
            foreach (var field in new[] { "framePath", "slotPath", "metadataPath" })
                Require(objectExists(ObjectPath(container, field)), "Container presentation lacks object evidence.");
            var slot = ObjectPath(container, "slotPath");
            var target = container.GetProperty("containerPath");
            if (role == "container-slot")
                Require(target.ValueKind == JsonValueKind.Null && sources.Contains(slot), "Container slot is not this asset's source.");
            else
            {
                var path = ObjectPath(container, "containerPath");
                Require(objectExists(path) && sources.Contains(path), "Default container is not this asset's source.");
            }
            containerSources.Add(ObjectPath(container, "metadataPath"));
        }
        return containerSources;
    }

    private static Reference ReadReference(JsonElement value)
    {
        Fields(value, "namespace", "key", "source", "cultureInvariant");
        return new(String(value, "namespace", allowEmpty: true), String(value, "key", allowEmpty: true),
            String(value, "source", allowEmpty: true), value.GetProperty("cultureInvariant").GetBoolean());
    }
}
