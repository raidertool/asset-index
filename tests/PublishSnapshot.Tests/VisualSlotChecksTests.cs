using System.Text.Json;
using System.Text.Json.Nodes;

namespace PublishSnapshot.Tests;

public sealed class VisualSlotChecksTests
{
    [Fact]
    public void LabelSourcesComeFromTheRecordedMembershipRelation()
    {
        Assert.Equal(["/Game/Nav.Nav"], Validate(Slot()));
    }

    [Theory]
    [InlineData("different-slot")]
    [InlineData("missing-label")]
    [InlineData("no-members")]
    [InlineData("missing-item")]
    [InlineData("missing-member-metadata")]
    [InlineData("duplicate-member")]
    [InlineData("duplicate-relation")]
    public void BrokenOrRepeatedRelationsAreRejected(string failure)
    {
        var slot = Slot();
        switch (failure)
        {
            case "different-slot": slot["slotPath"] = "/Game/Other.Other"; break;
            case "missing-label": slot["metadataPath"] = "/Game/Missing.Missing"; break;
            case "no-members": slot["members"]!.AsArray().Clear(); break;
            case "missing-item": slot["members"]![0]!["itemPath"] = "/Game/Missing.Missing"; break;
            case "missing-member-metadata": slot["members"]![0]!["metadataPath"] = "/Game/Missing.Missing"; break;
            case "duplicate-member": slot["members"]!.AsArray().Add(slot["members"]![0]!.DeepClone()); break;
        }
        Assert.Throws<InvalidDataException>(() => failure == "duplicate-relation"
            ? Validate(slot, slot.DeepClone()) : Validate(slot));
    }

    private static JsonObject Slot() => new()
    {
        ["slotPath"] = "/Game/Slot.Slot",
        ["typeTag"] = "UI.Category",
        ["metadataPath"] = "/Game/Nav.Nav",
        ["members"] = new JsonArray(new JsonObject
        {
            ["itemPath"] = "/Game/Item.Item",
            ["metadataPath"] = "/Game/ItemUi.ItemUi"
        })
    };

    private static HashSet<string> Validate(params JsonNode[] slots)
    {
        using var json = JsonDocument.Parse(new JsonArray(slots).ToJsonString());
        return VisualSlotChecks.Read(json.RootElement, ["/Game/Slot.Slot"],
            new HashSet<string> { "/Game/Slot.Slot", "/Game/Nav.Nav", "/Game/Item.Item", "/Game/ItemUi.ItemUi" }.Contains);
    }
}
