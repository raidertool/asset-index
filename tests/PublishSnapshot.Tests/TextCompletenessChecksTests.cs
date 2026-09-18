using System.Text.Json;
using System.Text.Json.Nodes;
using static PublishSnapshot.Tests.IdentityFixture;

namespace PublishSnapshot.Tests;

public sealed class TextCompletenessChecksTests
{
    [Fact]
    public void OmittingTheNpcNameCannotPromoteGenericItemText()
    {
        using var fixture = new Names("NPCItemDataAsset");
        var generic = fixture.Add("metadata", "/Game/Generic.Generic", "UIGameplayItemMetaDataItem", "ItemName", "Juanito");
        var npc = fixture.Add("metadata", "/Game/Npc.Npc", "UINPCMetaDataItem", "DisplayName", "Ermal");
        fixture.Select(npc);
        fixture.Validate();

        fixture.Candidates.Remove(npc);
        fixture.Select(generic);
        fixture.ValidateSelection(); // Supplied candidates alone make this look valid.
        Assert.Contains("/Game/Npc.Npc.DisplayName", Assert.Throws<InvalidDataException>(fixture.Validate).Message);
    }

    [Theory]
    [InlineData("Stash Expansion")]
    [InlineData("")]
    public void OmittingAnAuthoredNameIncludingEmptyCannotActivateARootFallback(string directName)
    {
        using var fixture = new Names();
        var direct = fixture.Add("metadata", "/Game/Direct.Direct", "UIGameplayItemMetaDataItem", "ItemName", directName);
        var root = fixture.Add("inventory-root", "/Game/Category.Category", "UIInventoryContainerMetaDataItem", "ContainerName", "Stash");
        fixture.Select(directName.Length == 0 ? null : direct);
        fixture.Validate();
        fixture.ValidateSelection();

        fixture.Candidates.Remove(direct);
        fixture.Select(root);
        fixture.ValidateSelection();
        Assert.Contains("Missing text candidate metadata: /Game/Direct.Direct.ItemName", Assert.Throws<InvalidDataException>(fixture.Validate).Message);
    }

    [Theory]
    [InlineData("container")]
    [InlineData("visual-slot")]
    [InlineData("inventory-root")]
    public void AClaimedContextCannotDropItsAuthoredLabel(string kind)
    {
        using var fixture = new Names();
        var type = kind == "visual-slot" ? "UICharacterCustomizationQuickNavTabMetaDataItem" : "UIInventoryContainerMetaDataItem";
        var field = kind == "visual-slot" ? "DisplayName" : "ContainerName";
        var label = fixture.Add(kind, "/Game/Category.Category", type, field, "Label");
        fixture.Validate();

        fixture.Candidates.Clear();
        Assert.Contains("Missing text candidate " + kind, Assert.Throws<InvalidDataException>(fixture.Validate).Message);
    }

    [Fact]
    public void AnInheritedNameStillRequiresItsCandidate()
    {
        using var fixture = new Names();
        var name = fixture.Add("metadata", "/Game/Child.Child", "UIGameplayItemMetaDataItem", "ItemName", "Inherited");
        var child = fixture.Fixture.Objects.Single(value => value["path"]!.GetValue<string>() == "/Game/Child.Child");
        child["properties"]!.AsArray().Clear();
        var template = fixture.Fixture.Object("/Game/Template.Template", "UIGameplayItemMetaDataItem");
        Header(template, "ItemName", "TextProperty");
        child["references"]![1]!["targetPath"] = "/Game/Template.Template";
        child["references"]![1]!["isNull"] = false;
        name["definedAt"] = "/Game/Template.Template";
        fixture.Validate();

        fixture.Candidates.Clear();
        Assert.Contains("/Game/Child.Child.ItemName", Assert.Throws<InvalidDataException>(fixture.Validate).Message);
    }

    [Fact]
    public void RepeatedContextOwnersNeedOneCandidate()
    {
        using var fixture = new Names();
        fixture.Add("inventory-root", "/Game/Category.Category", "UIInventoryContainerMetaDataItem", "ContainerName", "Stash");
        var roots = fixture.Presentation["inventoryRoots"]!.AsArray();
        roots.Add(roots[0]!.DeepClone());
        fixture.Validate();
    }

    [Fact]
    public void DuplicateCandidatesAreNotACompleteDeterministicExtraction()
    {
        using var fixture = new Names();
        var candidate = fixture.Add("metadata", "/Game/Direct.Direct", "UIGameplayItemMetaDataItem", "ItemName", "Name");
        fixture.Candidates.Add(candidate.DeepClone());
        Assert.Contains("Duplicate text candidate", Assert.Throws<InvalidDataException>(fixture.Validate).Message);
    }

    private sealed class Names : IDisposable
    {
        private const string Definition = "/Game/Definition.Definition";
        public IdentityFixture Fixture { get; } = new();
        public JsonObject Asset { get; }
        public JsonObject Presentation => Asset["presentation"]!.AsObject();
        public JsonArray Candidates => Presentation["candidates"]!.AsArray();

        public Names(string definitionType = "InventoryContainerSlotDataAsset")
        {
            Fixture.AddClass(definitionType, "ItemDataAssetBase");
            Fixture.AddClass("UIGameplayItemMetaDataItem", "UIMetaDataItem", ("ItemName", "TextProperty"), ("Description", "TextProperty"));
            Fixture.AddClass("UINPCMetaDataItem", "UIMetaDataItem", ("DisplayName", "TextProperty"), ("Description", "TextProperty"),
                ("LocationName", "TextProperty"), ("ObscuredDisplayName", "TextProperty"),
                ("ObscuredDescription", "TextProperty"), ("ObscuredLocationName", "TextProperty"));
            Fixture.AddClass("UIInventoryContainerMetaDataItem", "UIMetaDataItem", ("ContainerName", "TextProperty"));
            Fixture.AddClass("UICharacterCustomizationQuickNavTabMetaDataItem", "UIMetaDataItem", ("DisplayName", "TextProperty"));
            Asset = Catalog("41", [Fixture.Object(Definition, definitionType)]);
            Asset["presentation"] = new JsonObject
            {
                ["name"] = null,
                ["description"] = null,
                ["candidates"] = new JsonArray(),
                ["containers"] = new JsonArray(),
                ["visualSlots"] = new JsonArray(),
                ["inventoryRoots"] = new JsonArray()
            };
        }

        public JsonObject Add(string kind, string path, string type, string field, string value)
        {
            var source = Fixture.Object(path, type);
            Header(source, field, "TextProperty");
            if (kind == "metadata") Asset["metadata"]!.AsArray().Add(Source(source));
            else if (kind == "inventory-root") Presentation["inventoryRoots"]!.AsArray().Add(new JsonObject
            {
                ["role"] = "container-slot",
                ["containerType"] = "ENewInventoryContainerType::Stash",
                ["rootPath"] = Definition,
                ["rootField"] = "StashSlot",
                ["slotPath"] = Definition,
                ["containerPath"] = null,
                ["metadataPath"] = path
            });
            else Presentation[kind == "container" ? "containers" : "visualSlots"]!.AsArray()
                .Add(new JsonObject { ["metadataPath"] = path });
            var candidate = new JsonObject
            {
                ["sourceKind"] = kind,
                ["sourcePath"] = path,
                ["sourceClass"] = type,
                ["field"] = field,
                ["role"] = "display-name",
                ["definedAt"] = path,
                ["reference"] = new JsonObject { ["namespace"] = "", ["key"] = "", ["source"] = value, ["cultureInvariant"] = true }
            };
            Candidates.Add(candidate);
            return candidate;
        }

        public void Select(JsonObject? candidate) => Presentation["name"] = candidate?["reference"]?.DeepClone();

        public void Validate()
        {
            var context = Fixture.Read(TextRoleChecks.RootFields.ToArray()).Context;
            using var document = JsonDocument.Parse(new JsonArray(Asset.DeepClone()).ToJsonString());
            TextRoleChecks.Validate(document.RootElement, context);
        }

        public void ValidateSelection()
        {
            using var document = JsonDocument.Parse(Asset.ToJsonString());
            var sources = Asset["definitions"]!.AsArray().Concat(Asset["metadata"]!.AsArray())
                .Select(value => value!["path"]!.GetValue<string>()).ToHashSet(StringComparer.Ordinal);
            var discovered = Fixture.Objects.Select(value => value["path"]!.GetValue<string>()).ToHashSet(StringComparer.Ordinal);
            PresentationChecks.Validate(document.RootElement, sources, discovered.Contains);
        }

        public void Dispose() => Fixture.Dispose();
    }
}
