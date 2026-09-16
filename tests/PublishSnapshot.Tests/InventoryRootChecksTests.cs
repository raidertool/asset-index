using System.Text.Json;
using System.Text.Json.Nodes;

namespace PublishSnapshot.Tests;

public sealed class InventoryRootChecksTests
{
    [Theory]
    [InlineData("container-slot")]
    [InlineData("default-container")]
    [InlineData("allowed-container")]
    public void RootRelationsRetainTheirLabelSource(string role)
    {
        var relation = Root();
        relation["role"] = role;
        relation["containerPath"] = role == "container-slot" ? null : "/Game/Container.Container";

        Assert.Equal(["/Game/UI.UI"], Validate(relation));
    }

    [Theory]
    [InlineData("rootPath", "/Game/Missing.Missing")]
    [InlineData("slotPath", "/Game/Missing.Missing")]
    [InlineData("metadataPath", "/Game/Missing.Missing")]
    [InlineData("rootField", "UnknownSlot")]
    [InlineData("rootField", "AugmentSlot")]
    [InlineData("containerType", "ENewInventoryContainerType::Augment")]
    [InlineData("role", "unknown")]
    [InlineData("containerPath", "/Game/Container.Container")]
    public void MissingSourcesAndInconsistentRelationsFail(string field, string value)
    {
        var relation = Root();
        relation[field] = value;

        Assert.Throws<InvalidDataException>(() => Validate(relation));
    }

    [Fact]
    public void DuplicateRelationsAreRejected()
    {
        var relation = Root();

        Assert.Throws<InvalidDataException>(() => Validate(relation, relation.DeepClone()));
    }

    [Fact]
    public void ExistingUnrelatedContainerCannotBecomeTheAssetSource()
    {
        var relation = Root();
        relation["role"] = "allowed-container";
        relation["containerPath"] = "/Game/Other.Other";

        Assert.Throws<InvalidDataException>(() => Validate(relation));
    }

    private static JsonObject Root() => new()
    {
        ["role"] = "container-slot",
        ["containerType"] = "ENewInventoryContainerType::Stash",
        ["rootPath"] = "/Game/Root.Root",
        ["rootField"] = "BonusStashSlot",
        ["slotPath"] = "/Game/Slot.Slot",
        ["containerPath"] = null,
        ["metadataPath"] = "/Game/UI.UI"
    };

    private static HashSet<string> Validate(params JsonNode[] roots)
    {
        using var json = JsonDocument.Parse(new JsonArray(roots).ToJsonString());
        return InventoryRootChecks.Read(json.RootElement, ["/Game/Slot.Slot", "/Game/Container.Container"],
            ["/Game/Root.Root", "/Game/Slot.Slot", "/Game/Container.Container", "/Game/Other.Other", "/Game/UI.UI"]);
    }
}
