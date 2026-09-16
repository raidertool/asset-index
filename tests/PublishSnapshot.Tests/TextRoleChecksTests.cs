using System.Text.Json;
using System.Text.Json.Nodes;
using static PublishSnapshot.Tests.IdentityFixture;

namespace PublishSnapshot.Tests;

public sealed class TextRoleChecksTests
{
    private const string Source = "/Game/Slot.Slot";
    private const string Template = "/Game/Template.Template";

    [Fact]
    public void SlotTooltipCannotBePromotedToDisplayName()
    {
        using var fixture = SlotFixture();
        Header(fixture.Object(Source, "UIInventorySlotMetaDataItem"), "EmptySlotTooltipText", "TextProperty");
        Validate(fixture, "UIInventorySlotMetaDataItem", "EmptySlotTooltipText", "tooltip");
        Assert.Throws<InvalidDataException>(() => Validate(fixture, "UIInventorySlotMetaDataItem", "EmptySlotTooltipText", "display-name"));
    }

    [Fact]
    public void UnlockDescriptionDoesNotBecomeAnUnlockTitle()
    {
        using var fixture = new IdentityFixture();
        fixture.AddClass("UIUnlockMetaDataItem", "UIMetaDataItem", ("UnlockTitle", "TextProperty"),
            ("UnlockDescription", "TextProperty"), ("NavigationText", "TextProperty"));
        Header(fixture.Object(Source, "UIUnlockMetaDataItem"), "UnlockDescription", "TextProperty");
        Validate(fixture, "UIUnlockMetaDataItem", "UnlockDescription", "unlock-description");
        Assert.Throws<InvalidDataException>(() => Validate(fixture, "UIUnlockMetaDataItem", "UnlockDescription", "title"));
    }

    [Theory]
    [InlineData("UIInventorySlotMetaDataItem", "Missing", "tooltip")]
    [InlineData("UIGameplayItemMetaDataItem", "EmptySlotTooltipText", "tooltip")]
    public void FieldOrClassClaimsCannotInventATextRole(string type, string field, string role)
    {
        using var fixture = SlotFixture();
        Header(fixture.Object(Source, "UIInventorySlotMetaDataItem"), "EmptySlotTooltipText", "TextProperty");
        Assert.Throws<InvalidDataException>(() => Validate(fixture, type, field, role));
    }

    [Fact]
    public void UnknownClassDoesNotAcquireARoleFromTheFieldName()
    {
        using var fixture = new IdentityFixture();
        fixture.AddClass("Other", "DataAsset", ("ItemName", "TextProperty"));
        Header(fixture.Object(Source, "Other"), "ItemName", "TextProperty");
        Assert.Throws<InvalidDataException>(() => Validate(fixture, "Other", "ItemName", "display-name"));
    }

    [Fact]
    public void InheritedTextUsesTheFirstCompatibleTemplateOwner()
    {
        using var fixture = SlotFixture();
        fixture.Object(Source, "UIInventorySlotMetaDataItem", Template);
        Header(fixture.Object(Template, "UIInventorySlotMetaDataItem"), "EmptySlotTooltipText", "TextProperty");
        Validate(fixture, "UIInventorySlotMetaDataItem", "emptyslottooltiptext", "tooltip", Template);
        Assert.Throws<InvalidDataException>(() => Validate(fixture, "UIInventorySlotMetaDataItem", "EmptySlotTooltipText", "tooltip", Source));
    }

    [Fact]
    public void UnrelatedTemplateCannotSupplyEvenTheSameRoleAndField()
    {
        using var fixture = SlotFixture();
        fixture.AddClass("Other", "DataAsset", ("EmptySlotTooltipText", "TextProperty"));
        fixture.Object(Source, "UIInventorySlotMetaDataItem", Template);
        Header(fixture.Object(Template, "Other"), "EmptySlotTooltipText", "TextProperty");
        Assert.Throws<InvalidDataException>(() => Validate(fixture, "UIInventorySlotMetaDataItem", "EmptySlotTooltipText", "tooltip", Template));
    }

    [Theory]
    [InlineData("declaration")]
    [InlineData("decoded")]
    public void TextRoleRequiresBothMappedAndDecodedTextProperty(string mutation)
    {
        using var fixture = SlotFixture();
        var source = fixture.Object(Source, "UIInventorySlotMetaDataItem");
        Header(source, "EmptySlotTooltipText", "TextProperty");
        if (mutation == "declaration") fixture.Mappings.Types["UIInventorySlotMetaDataItem"].Properties[0].MappingType.Type = "StrProperty";
        else source["properties"]![0]!["type"] = "StrProperty";
        Assert.Throws<InvalidDataException>(() => Validate(fixture, "UIInventorySlotMetaDataItem", "EmptySlotTooltipText", "tooltip"));
    }

    [Fact]
    public void NearestNativePolicyDoesNotMergeUnrelatedBaseLabels()
    {
        using var fixture = new IdentityFixture();
        fixture.AddClass("UIGameplayItemMetaDataItem", "UIMetaDataItem", ("ItemName", "TextProperty"), ("Description", "TextProperty"));
        fixture.AddClass("UINPCMetaDataItem", "UIGameplayItemMetaDataItem", ("DisplayName", "TextProperty"),
            ("LocationName", "TextProperty"), ("ObscuredDisplayName", "TextProperty"),
            ("ObscuredDescription", "TextProperty"), ("ObscuredLocationName", "TextProperty"));
        var source = fixture.Object(Source, "UINPCMetaDataItem");
        Header(source, "DisplayName", "TextProperty");
        Header(source, "ItemName", "TextProperty");
        Validate(fixture, "UINPCMetaDataItem", "DisplayName", "display-name");
        Assert.Throws<InvalidDataException>(() => Validate(fixture, "UINPCMetaDataItem", "ItemName", "display-name"));
    }

    [Theory]
    [InlineData("container")]
    [InlineData("inventory-root")]
    public void ContainerLabelRequiresItsSeparateContextualContract(string kind)
    {
        using var fixture = new IdentityFixture();
        fixture.AddClass("UIInventoryContainerMetaDataItem", "UIMetaDataItem", ("ContainerName", "TextProperty"));
        Header(fixture.Object(Source, "UIInventoryContainerMetaDataItem"), "ContainerName", "TextProperty");
        Validate(fixture, "UIInventoryContainerMetaDataItem", "ContainerName", "display-name", kind: kind);
        Assert.Throws<InvalidDataException>(() => Validate(fixture, "UIInventoryContainerMetaDataItem", "ContainerName", "display-name"));
        Assert.Throws<InvalidDataException>(() => Validate(fixture, "UIInventoryContainerMetaDataItem", "ContainerName", "description", kind: kind));
    }

    [Fact]
    public void VisualSlotLabelRequiresQuickNavigationOwner()
    {
        using var fixture = new IdentityFixture();
        fixture.AddClass("UICharacterCustomizationQuickNavTabMetaDataItem", "UIMetaDataItem", ("DisplayName", "TextProperty"));
        Header(fixture.Object(Source, "UICharacterCustomizationQuickNavTabMetaDataItem"), "DisplayName", "TextProperty");
        Validate(fixture, "UICharacterCustomizationQuickNavTabMetaDataItem", "DisplayName", "display-name", kind: "visual-slot");
        Assert.Throws<InvalidDataException>(() => Validate(fixture, "UICharacterCustomizationQuickNavTabMetaDataItem", "DisplayName", "description", kind: "visual-slot"));
        Assert.Throws<InvalidDataException>(() => Validate(fixture, "UICharacterCustomizationQuickNavTabMetaDataItem", "DisplayName", "display-name", kind: "container"));
        Assert.Throws<InvalidDataException>(() => Validate(fixture, "UICharacterCustomizationQuickNavTabMetaDataItem", "DisplayName", "display-name", kind: "inventory-root"));
    }

    private static IdentityFixture SlotFixture()
    {
        var fixture = new IdentityFixture();
        fixture.AddClass("UIInventorySlotMetaDataItem", "UIMetaDataItem", ("EmptySlotTooltipText", "TextProperty"));
        return fixture;
    }

    private static void Validate(IdentityFixture fixture, string type, string field, string role, string definedAt = Source, string kind = "metadata")
    {
        var context = fixture.Read(TextRoleChecks.RootFields.ToArray()).Context;
        using var document = JsonDocument.Parse(new JsonArray(new JsonObject
        {
            ["definitions"] = kind == "definition" ? new JsonArray(new JsonObject { ["path"] = Source }) : new JsonArray(),
            ["metadata"] = kind == "metadata" ? new JsonArray(new JsonObject { ["path"] = Source }) : new JsonArray(),
            ["presentation"] = new JsonObject
            {
                ["containers"] = Context("container"),
                ["visualSlots"] = Context("visual-slot"),
                ["inventoryRoots"] = Context("inventory-root"),
                ["candidates"] = new JsonArray(new JsonObject
                {
                    ["sourcePath"] = Source,
                    ["sourceClass"] = type,
                    ["field"] = field,
                    ["role"] = role,
                    ["definedAt"] = definedAt,
                    ["sourceKind"] = kind
                })
            }
        }).ToJsonString());
        TextRoleChecks.Validate(document.RootElement, context);

        JsonArray Context(string expectedKind) => kind == expectedKind
            ? new JsonArray(new JsonObject { ["metadataPath"] = Source }) : new JsonArray();
    }
}
