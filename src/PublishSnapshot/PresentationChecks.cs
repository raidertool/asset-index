using System.Text.Json;
using static PublishSnapshot.Preview;
using static PublishSnapshot.ResourceEvidence;

namespace PublishSnapshot;

internal static class PresentationChecks
{
    private sealed record Reference(string Namespace, string Key, string Source, bool CultureInvariant);

    public static void Validate(JsonElement asset, HashSet<string> sources, HashSet<string> discovered)
    {
        var presentation = asset.GetProperty("presentation");
        Fields(presentation, "name", "description", "candidates", "containers");
        var containerSources = ReadContainers(presentation.GetProperty("containers"), sources, discovered);
        var owners = new Dictionary<string, HashSet<string>>
        {
            ["definition"] = asset.GetProperty("definitions").EnumerateArray().Select(source => ObjectPath(source, "path")).ToHashSet(StringComparer.Ordinal),
            ["metadata"] = asset.GetProperty("metadata").EnumerateArray().Select(source => ObjectPath(source, "path")).ToHashSet(StringComparer.Ordinal),
            ["container"] = containerSources
        };
        var candidates = new List<(string Kind, string Role, Reference Value)>();
        foreach (var candidate in presentation.GetProperty("candidates").EnumerateArray())
        {
            Fields(candidate, "role", "sourceKind", "sourcePath", "sourceClass", "field", "definedAt", "reference");
            var kind = String(candidate, "sourceKind");
            Require(owners.ContainsKey(kind), "Unknown text source kind.");
            var path = ObjectPath(candidate, "sourcePath");
            Require(owners[kind].Contains(path), "Text candidate is not linked to this asset with its declared source kind.");
            Require(discovered.Contains(ObjectPath(candidate, "definedAt")), "Text candidate defining object is missing.");
            String(candidate, "sourceClass"); String(candidate, "field");
            candidates.Add((kind, String(candidate, "role"), ReadReference(candidate.GetProperty("reference"))));
        }
        foreach (var field in new[] { "name", "description" })
        {
            var value = presentation.GetProperty(field);
            var selected = value.ValueKind == JsonValueKind.Null ? null : ReadReference(value);
            var roles = field == "name" ? new[] { "display-name", "title", "short-name" } : ["description", "tooltip"];
            Require(selected == Select(candidates, roles), $"Selected {field} does not match candidate precedence.");
        }
    }

    private static Reference? Select(IReadOnlyList<(string Kind, string Role, Reference Value)> candidates, string[] roles)
    {
        foreach (var kind in new[] { "metadata", "definition", "container" })
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

    private static HashSet<string> ReadContainers(JsonElement containers, HashSet<string> sources, HashSet<string> discovered)
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
                Require(discovered.Contains(ObjectPath(container, field)), "Container presentation lacks object evidence.");
            var slot = ObjectPath(container, "slotPath");
            var target = container.GetProperty("containerPath");
            if (role == "container-slot")
                Require(target.ValueKind == JsonValueKind.Null && sources.Contains(slot), "Container slot is not this asset's source.");
            else
            {
                var path = ObjectPath(container, "containerPath");
                Require(discovered.Contains(path) && sources.Contains(path), "Default container is not this asset's source.");
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
