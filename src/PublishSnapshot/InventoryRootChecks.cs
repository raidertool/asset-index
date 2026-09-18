using System.Text.Json;
using AssetIndex;
using static PublishSnapshot.Preview;
using static PublishSnapshot.ResourceEvidence;

namespace PublishSnapshot;

internal static class InventoryRootChecks
{
    public static HashSet<string> Read(JsonElement roots, HashSet<string> sources, Func<string, bool> objectExists)
    {
        var labels = new HashSet<string>(StringComparer.Ordinal);
        var associations = new HashSet<(string Root, string Field, string Slot, string? Container, string Label)>();
        foreach (var root in roots.EnumerateArray())
        {
            Fields(root, "role", "containerType", "rootPath", "rootField", "slotPath", "containerPath", "metadataPath");
            Require(InventoryRootPolicy.Categories.TryGetValue(String(root, "rootField"), out var category) &&
                category == String(root, "containerType"), "Inventory root category differs from its approved field policy.");
            var path = ObjectPath(root, "rootPath");
            var slot = ObjectPath(root, "slotPath");
            var label = ObjectPath(root, "metadataPath");
            Require(objectExists(path) && objectExists(slot) && objectExists(label),
                "Inventory root presentation lacks object evidence.");
            var role = String(root, "role");
            Require(role is "container-slot" or "default-container" or "allowed-container", "Unknown inventory root role.");
            string? container = null;
            if (role == "container-slot")
                Require(root.GetProperty("containerPath").ValueKind == JsonValueKind.Null && sources.Contains(slot),
                    "Inventory root slot is not this asset's source.");
            else
            {
                container = ObjectPath(root, "containerPath");
                Require(sources.Contains(container) && objectExists(container), "Inventory root container is not this asset's source.");
            }
            Require(associations.Add((path, String(root, "rootField"), slot, container, label)), "Duplicate inventory root association.");
            labels.Add(label);
        }
        return labels;
    }
}
