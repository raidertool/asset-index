using System.Text.Json;
using static PublishSnapshot.Preview;
using static PublishSnapshot.ResourceEvidence;

namespace PublishSnapshot;

// Structural checks only; decoded identity and query membership require their own proof.
internal static class InventoryRootChecks
{
    public static HashSet<string> Read(JsonElement roots, HashSet<string> sources, HashSet<string> discovered)
    {
        var labels = new HashSet<string>(StringComparer.Ordinal);
        var associations = new HashSet<(string Root, string Field, string Slot, string? Container, string Label)>();
        foreach (var root in roots.EnumerateArray())
        {
            Fields(root, "role", "containerType", "rootPath", "rootField", "slotPath", "containerPath", "metadataPath");
            var field = String(root, "rootField");
            Require(field is "StashSlot" or "ExpeditionStashSlot" or "BonusStashSlot" or "SecretStashSlot" or "AugmentSlot",
                "Unknown inventory root field.");
            var type = field == "AugmentSlot" ? "Augment" : "Stash";
            Require(String(root, "containerType") == "ENewInventoryContainerType::" + type, "Inventory root category differs from its field.");
            var path = ObjectPath(root, "rootPath");
            var slot = ObjectPath(root, "slotPath");
            var label = ObjectPath(root, "metadataPath");
            Require(discovered.Contains(path) && discovered.Contains(slot) && discovered.Contains(label),
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
                Require(sources.Contains(container) && discovered.Contains(container), "Inventory root container is not this asset's source.");
            }
            Require(associations.Add((path, field, slot, container, label)), "Duplicate inventory root association.");
            labels.Add(label);
        }
        return labels;
    }
}
