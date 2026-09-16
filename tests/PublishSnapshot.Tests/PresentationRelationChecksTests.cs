using System.Text.Json;
using System.Text.Json.Nodes;
using static PublishSnapshot.Tests.IdentityFixture;

namespace PublishSnapshot.Tests;

public sealed class PresentationRelationChecksTests
{
    [Theory]
    [InlineData("container-slot")]
    [InlineData("default-container")]
    public void ContainerLabelsFollowActualFrameEntryAndDefaultReferences(string role)
    {
        using var fixture = new Relations();
        var relation = fixture.Container(role);

        fixture.Validate(role == "container-slot" ? "41" : "42", "containers", relation);
    }

    [Theory]
    [InlineData("frame-reference")]
    [InlineData("frame-type")]
    [InlineData("metadata-type")]
    [InlineData("index")]
    [InlineData("default-reference")]
    [InlineData("wrong-id")]
    [InlineData("wrong-class")]
    public void ExistingObjectsCannotProveAnUnrelatedContainerLabel(string mutation)
    {
        using var fixture = new Relations();
        var relation = fixture.Container("default-container");
        var id = "42";
        switch (mutation)
        {
            case "frame-reference": relation["slotPath"] = Relations.OtherSlot; break;
            case "frame-type": relation["containerType"] = "ENewInventoryContainerType::Backpack"; break;
            case "metadata-type": fixture.Category["values"]![0]!["value"] = "ENewInventoryContainerType::Backpack"; break;
            case "index": relation["containerIndex"] = 1; break;
            case "default-reference": relation["containerPath"] = Relations.OtherItem; break;
            case "wrong-id": id = "900"; break;
            case "wrong-class": relation["metadataPath"] = Relations.Item; break;
        }

        Assert.Throws<InvalidDataException>(() => fixture.Validate(id, "containers", relation));
    }

    [Fact]
    public void InheritedFrameAndDefaultFieldsAreSupported()
    {
        using var fixture = new Relations();
        var relation = fixture.Container("default-container");
        var frame = fixture.Fixture.Object("/Game/InheritedFrame.InheritedFrame", "LoadoutFrameItemDataAsset", Relations.Frame);
        relation["framePath"] = frame["path"]!.GetValue<string>();

        fixture.Validate("42", "containers", relation);
    }

    [Fact]
    public void LocalNullDefaultMasksTheParentReference()
    {
        using var fixture = new Relations();
        var relation = fixture.Container("default-container");
        var slot = fixture.Fixture.Object("/Game/ChildSlot.ChildSlot", "InventoryContainerSlotDataAsset", Relations.Slot);
        Reference(slot, "DefaultContainer", null);
        fixture.FrameObject["references"]!.AsArray().Last()!["targetPath"] = slot["path"]!.GetValue<string>();
        relation["slotPath"] = slot["path"]!.GetValue<string>();

        Assert.Throws<InvalidDataException>(() => fixture.Validate("42", "containers", relation));
    }

    [Fact]
    public void VisualLabelsRequireTheCompleteQueryResultAndMatchingUiCategory()
    {
        using var fixture = new Relations();

        fixture.Validate("50", "visualSlots", fixture.Visual());
    }

    [Fact]
    public void EveryMatchingSkinIsAllowedWhenTheMemberListIsComplete()
    {
        using var fixture = new Relations();
        var (skin, ui) = fixture.AddSkin("Second", "71", Relations.UiType);
        var relation = fixture.Visual();
        relation["members"]!.AsArray().Add(new JsonObject
        {
            ["itemPath"] = skin["path"]!.GetValue<string>(), ["metadataPath"] = ui["path"]!.GetValue<string>()
        });

        fixture.Validate("50", "visualSlots", relation);
    }

    [Theory]
    [InlineData("missing-member")]
    [InlineData("duplicate-member")]
    [InlineData("extra-member")]
    [InlineData("wrong-item")]
    [InlineData("wrong-ui")]
    [InlineData("wrong-tag")]
    [InlineData("wrong-label")]
    [InlineData("wrong-id")]
    public void VisualRelationChangesCannotInventMembershipOrCategory(string mutation)
    {
        using var fixture = new Relations();
        var relation = fixture.Visual();
        var members = relation["members"]!.AsArray();
        var id = "50";
        switch (mutation)
        {
            case "missing-member": members.Clear(); break;
            case "duplicate-member": members.Add(members[0]!.DeepClone()); break;
            case "extra-member": members.Add(new JsonObject { ["itemPath"] = Relations.Item, ["metadataPath"] = Relations.SkinUi }); break;
            case "wrong-item": members[0]!["itemPath"] = Relations.Item; break;
            case "wrong-ui": members[0]!["metadataPath"] = Relations.Label; break;
            case "wrong-tag": relation["typeTag"] = "UI.Type.Other"; break;
            case "wrong-label": fixture.Navigation["values"]![0]!["value"] = "UI.Type.Other"; break;
            case "wrong-id": id = "999"; break;
        }

        Assert.Throws<InvalidDataException>(() => fixture.Validate(id, "visualSlots", relation));
    }

    [Theory]
    [InlineData("different-type")]
    [InlineData("missing-ui")]
    [InlineData("overridden-ui-id")]
    [InlineData("overridden-skin-id")]
    [InlineData("missing-tags")]
    public void OmittedMatchingSkinsStillParticipateInTheProof(string mutation)
    {
        using var fixture = new Relations();
        var (skin, ui) = fixture.AddSkin("Second", "71", mutation == "different-type" ? "UI.Type.Other" : Relations.UiType);
        switch (mutation)
        {
            case "missing-ui": fixture.Fixture.Objects.Remove(ui); break;
            case "overridden-ui-id": Boolean(ui, "bOverrideAssetId", true); Number(ui, "OverrideAssetId", "901"); break;
            case "overridden-skin-id": Boolean(skin, "bOverrideItemAssetId", true); Number(skin, "OverrideItemAssetId", "902"); break;
            case "missing-tags":
                skin["properties"]!.AsArray().RemoveAt(1);
                skin["values"]!.AsArray().Clear();
                break;
        }

        Assert.Throws<InvalidDataException>(() => fixture.Validate("50", "visualSlots", fixture.Visual()));
    }

    [Fact]
    public void UnrelatedUnknownClassCannotSilentlyExcludeASkin()
    {
        using var fixture = new Relations();
        fixture.Fixture.Object("/Game/Unknown.Unknown", "UnmappedSkin");

        Assert.Throws<InvalidDataException>(() => fixture.Validate("50", "visualSlots", fixture.Visual()));
    }

    [Fact]
    public void IncompatibleTemplateCannotSupplyAFrameField()
    {
        using var fixture = new Relations();
        var child = fixture.Fixture.Object("/Game/Fake.Fake", "LoadoutFrameItemDataAsset", Relations.Frame);
        fixture.FrameObject["class"] = "DataAsset";
        fixture.FrameObject["references"]![0]!["targetPath"] = "/Script/Test.DataAsset";
        fixture.Fixture.Exports.Single(row => row["path"]!.GetValue<string>() == Relations.Frame)["classPath"] = "/Script/Test.DataAsset";
        var relation = fixture.Container("container-slot");
        relation["framePath"] = child["path"]!.GetValue<string>();

        Assert.Throws<InvalidDataException>(() => fixture.Validate("41", "containers", relation));
    }

    private sealed class Relations : IDisposable
    {
        public const string Frame = "/Game/Frame.Frame", Slot = "/Game/Slot.Slot", Item = "/Game/Item.Item";
        public const string OtherSlot = "/Game/OtherSlot.OtherSlot", OtherItem = "/Game/OtherItem.OtherItem";
        public const string VisualSlot = "/Game/VisualSlot.VisualSlot", Skin = "/Game/First.First", SkinUi = "/Game/FirstUi.FirstUi";
        public const string Label = "/Game/Label.Label", UiType = "UI.Type.Skin";
        public IdentityFixture Fixture { get; } = new();
        public JsonObject FrameObject { get; }
        public JsonObject Category { get; }
        public JsonObject Navigation { get; }

        public Relations()
        {
            Fixture.AddClass("LoadoutFrameItemDataAsset", "ItemDataAssetBase", ("Containers", "ArrayProperty"));
            Fixture.AddClass("InventoryContainerSlotDataAsset", "ItemDataAssetBase", ("DefaultContainer", "ObjectProperty"));
            Fixture.AddClass("InventoryContainerItemDataAsset", "ItemDataAssetBase");
            Fixture.AddClass("UIInventoryContainerMetaDataItem", "Object", ("ContainerType", "EnumProperty"));
            Fixture.AddClass("CharacterVisualSlotOnlineItemDataAsset", "ItemDataAssetBase", ("ItemsQuery", "StructProperty"));
            Fixture.AddClass("CharacterVisualSkinOnlineItemDataAsset", "ItemDataAssetBase", ("Tags", "StructProperty"));
            Fixture.AddClass("UICharacterVisualSkinMetaDataItem", "UIMetaDataItem", ("TypeTag", "StructProperty"));
            Fixture.AddClass("UICharacterCustomizationQuickNavTabMetaDataItem", "Object", ("CharacterCustomizationTypeTag", "StructProperty"));
            ItemObject(Item, "InventoryContainerItemDataAsset", "42");
            ItemObject(OtherItem, "InventoryContainerItemDataAsset", "43");
            var slot = ItemObject(Slot, "InventoryContainerSlotDataAsset", "41");
            Reference(slot, "DefaultContainer", Item);
            ItemObject(OtherSlot, "InventoryContainerSlotDataAsset", "44");
            FrameObject = Fixture.Object(Frame, "LoadoutFrameItemDataAsset");
            var containers = Header(FrameObject, "Containers", "ArrayProperty");
            var type = Header(FrameObject, "Type", "EnumProperty", containers + "/0/Properties");
            Value(FrameObject, type, "EnumProperty", "name", "ENewInventoryContainerType::Armor");
            var target = Header(FrameObject, "ContainerSlotDataAsset", "ObjectProperty", containers + "/0/Properties");
            FrameObject["references"]!.AsArray().Add(Link(target, Slot, "property", "hard"));
            Category = Fixture.Object("/Game/Category.Category", "UIInventoryContainerMetaDataItem");
            Scalar(Category, "ContainerType", "EnumProperty", "name", "ENewInventoryContainerType::Armor");
            var visual = ItemObject(VisualSlot, "CharacterVisualSlotOnlineItemDataAsset", "50");
            Query(visual);
            AddSkin("First", "70", UiType);
            Navigation = Fixture.Object(Label, "UICharacterCustomizationQuickNavTabMetaDataItem");
            Tag(Navigation, "CharacterCustomizationTypeTag", UiType);
        }

        public (JsonObject Skin, JsonObject Ui) AddSkin(string name, string id, string type)
        {
            var skin = ItemObject("/Game/" + name + "." + name, "CharacterVisualSkinOnlineItemDataAsset", id);
            var tags = Header(skin, "Tags", "StructProperty");
            Value(skin, tags + "/GameplayTags/0/TagName", "FName", "name", "Item.Skin.Child");
            var ui = Fixture.Object("/Game/" + name + "Ui." + name + "Ui", "UICharacterVisualSkinMetaDataItem");
            Reference(ui, "PersistenceDataAsset", "/Game/Persistence" + id + ".Persistence" + id);
            Tag(ui, "TypeTag", type);
            return (skin, ui);
        }

        public JsonObject Container(string role) => new()
        {
            ["role"] = role, ["containerType"] = "ENewInventoryContainerType::Armor", ["framePath"] = Frame,
            ["containerIndex"] = 0, ["slotPath"] = Slot, ["containerPath"] = role == "container-slot" ? null : Item,
            ["metadataPath"] = "/Game/Category.Category"
        };

        public JsonObject Visual() => new()
        {
            ["slotPath"] = VisualSlot, ["typeTag"] = UiType, ["metadataPath"] = Label,
            ["members"] = new JsonArray(new JsonObject { ["itemPath"] = Skin, ["metadataPath"] = SkinUi })
        };

        public void Validate(string id, string family, JsonObject relation)
        {
            var (evidence, _, context) = Fixture.Read(PresentationRelationChecks.RootFields);
            var presentation = new JsonObject { ["containers"] = new JsonArray(), ["visualSlots"] = new JsonArray() };
            presentation[family]!.AsArray().Add(relation.DeepClone());
            using var json = JsonDocument.Parse(new JsonArray(new JsonObject { ["id"] = id, ["presentation"] = presentation }).ToJsonString());
            new PresentationRelationChecks(evidence, context).Validate(json.RootElement);
        }

        private JsonObject ItemObject(string path, string type, string id)
        {
            var persistencePath = "/Game/Persistence" + id + ".Persistence" + id;
            var persistence = Fixture.Object(persistencePath, "PersistenceDataAsset");
            Number(persistence, "AssetId", id);
            var item = Fixture.Object(path, type);
            Reference(item, "PersistenceDataAsset", persistencePath);
            return item;
        }

        private static void Tag(JsonObject row, string name, string value)
        {
            var field = Header(row, name, "StructProperty");
            var tag = Header(row, "TagName", "NameProperty", field + "/Properties");
            Value(row, tag, "NameProperty", "name", value);
        }

        private static void Query(JsonObject row)
        {
            var field = Header(row, "ItemsQuery", "StructProperty");
            var dictionary = Header(row, "TagDictionary", "ArrayProperty", field + "/Properties");
            var tag = Header(row, "TagName", "NameProperty", dictionary + "/0/Properties");
            Value(row, tag, "NameProperty", "name", "Item.Skin");
            var tokens = Header(row, "QueryTokenStream", "ArrayProperty", field + "/Properties");
            Value(row, tokens, "ByteProperty[]", "binary-base64", Convert.ToBase64String([0, 1, 1, 1, 0]));
        }

        public void Dispose() => Fixture.Dispose();
    }
}
