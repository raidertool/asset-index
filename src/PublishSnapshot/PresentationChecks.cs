using System.Text.Json;
using static PublishSnapshot.Preview;
using static PublishSnapshot.ResourceEvidence;

namespace PublishSnapshot;

internal static class PresentationChecks
{
    private sealed record Reference(string Namespace, string Key, string Source, bool CultureInvariant);

    public static void Validate(JsonElement presentation, HashSet<string> sources, HashSet<string> discovered)
    {
        Fields(presentation, "name", "description", "candidates", "containers");
        var containerSources = ReadContainers(presentation.GetProperty("containers"), sources, discovered);
        var candidates = new List<(string Role, Reference Value)>();
        foreach (var candidate in presentation.GetProperty("candidates").EnumerateArray())
        {
            Fields(candidate, "role", "sourceKind", "sourcePath", "sourceClass", "field", "definedAt", "reference");
            var kind = String(candidate, "sourceKind");
            Require(kind is "definition" or "metadata" or "container", "Unknown text source kind.");
            var path = ObjectPath(candidate, "sourcePath");
            Require((kind == "container" ? containerSources : sources).Contains(path), "Text candidate is not linked to this asset.");
            Require(discovered.Contains(ObjectPath(candidate, "definedAt")), "Text candidate defining object is missing.");
            String(candidate, "sourceClass"); String(candidate, "field");
            candidates.Add((String(candidate, "role"), ReadReference(candidate.GetProperty("reference"))));
        }
        foreach (var field in new[] { "name", "description" })
        {
            if (presentation.GetProperty(field).ValueKind == JsonValueKind.Null) continue;
            var selected = ReadReference(presentation.GetProperty(field));
            var roles = field == "name" ? new[] { "display-name", "title", "short-name" } : ["description", "tooltip"];
            Require(candidates.Any(candidate => roles.Contains(candidate.Role) && candidate.Value == selected), "Selected text lacks a matching candidate.");
        }
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
