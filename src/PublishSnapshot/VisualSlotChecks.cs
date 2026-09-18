using System.Text.Json;
using static PublishSnapshot.Preview;
using static PublishSnapshot.ResourceEvidence;

namespace PublishSnapshot;

internal static class VisualSlotChecks
{
    public static HashSet<string> Read(JsonElement slots, HashSet<string> sources, Func<string, bool> objectExists)
    {
        var labels = new HashSet<string>(StringComparer.Ordinal);
        var associations = new HashSet<(string Slot, string Label)>();
        foreach (var slot in slots.EnumerateArray())
        {
            Fields(slot, "slotPath", "typeTag", "metadataPath", "members");
            var path = ObjectPath(slot, "slotPath");
            var label = ObjectPath(slot, "metadataPath");
            Require(sources.Contains(path) && objectExists(label), "Visual-slot label lacks its asset or metadata source.");
            Require(associations.Add((path, label)), "Duplicate visual-slot label association.");
            String(slot, "typeTag");
            var members = new HashSet<(string Item, string Metadata)>();
            foreach (var member in slot.GetProperty("members").EnumerateArray())
            {
                Fields(member, "itemPath", "metadataPath");
                var item = ObjectPath(member, "itemPath");
                var metadata = ObjectPath(member, "metadataPath");
                Require(objectExists(item) && objectExists(metadata) && members.Add((item, metadata)),
                    "Visual-slot member is missing or duplicated.");
            }
            Require(members.Count > 0, "Visual-slot label has no matching members.");
            labels.Add(label);
        }
        return labels;
    }
}
