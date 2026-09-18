using System.Text.Json;
using System.Text.Json.Nodes;
using AssetIndex;
using static PublishSnapshot.Tests.IdentityFixture;

namespace PublishSnapshot.Tests;

public sealed class InventoryRootChecksTests
{
    [Theory]
    [InlineData("StashSlot")]
    [InlineData("ExpeditionStashSlot")]
    [InlineData("BonusStashSlot")]
    [InlineData("SecretStashSlot")]
    [InlineData("AugmentSlot")]
    public void ApprovedRootFieldsUseTheirCategoryWithTypedSlotIdentity(string field)
    {
        using var fixture = new RootFixture(field);
        fixture.Validate("41", fixture.Relation("container-slot"));
    }

    [Theory]
    [InlineData("default-container", "42")]
    [InlineData("allowed-container", "43")]
    public void ContainersRequireTheirDefaultReferenceOrMatchingQuery(string role, string id)
    {
        using var fixture = new RootFixture();
        fixture.Validate(id, fixture.Relation(role));
    }

    [Theory]
    [InlineData("root-reference")]
    [InlineData("root-field")]
    [InlineData("root-category")]
    [InlineData("root-declaration")]
    [InlineData("metadata-category")]
    [InlineData("default-reference")]
    [InlineData("slot-identity")]
    [InlineData("target-identity")]
    [InlineData("container-role")]
    [InlineData("slot-container")]
    public void ADeclaredRootRelationCannotContradictDecodedEvidence(string mutation)
    {
        using var fixture = new RootFixture();
        var relation = fixture.Relation("default-container");
        var id = "42";
        switch (mutation)
        {
            case "root-reference": fixture.Root["references"]!.AsArray().Last()!["targetPath"] = RootFixture.Default; break;
            case "root-field": relation["rootField"] = "MadeUpSlot"; break;
            case "root-category": relation["containerType"] = "ENewInventoryContainerType::Augment"; break;
            case "root-declaration": fixture.Fixture.Mappings.Types["InventoryTreeRootAsset"].Properties[0].MappingType.Type = "StrProperty"; break;
            case "metadata-category": fixture.Label["values"]![0]!["value"] = "ENewInventoryContainerType::Augment"; break;
            case "default-reference": relation["containerPath"] = RootFixture.Allowed; id = "43"; break;
            case "slot-identity": fixture.Slot["references"]!.AsArray()[2]!["isNull"] = true; fixture.Slot["references"]![2]!["targetPath"] = null; break;
            case "target-identity": id = "900"; break;
            case "container-role": relation["role"] = "fabricated-container"; break;
            case "slot-container": relation["role"] = "container-slot"; id = "41"; break;
        }
        Assert.Throws<InvalidDataException>(() => fixture.Validate(id, relation));
    }

    [Theory]
    [InlineData("root")]
    [InlineData("slot")]
    [InlineData("container")]
    [InlineData("metadata")]
    public void SameNamedFieldsOnUnrelatedNativeClassesCannotProveTheRelation(string target)
    {
        using var fixture = new RootFixture();
        var row = target switch
        {
            "root" => fixture.Root,
            "slot" => fixture.Slot,
            "container" => fixture.DefaultItem,
            _ => fixture.Label
        };
        row["class"] = "DataAsset";
        row["references"]![0]!["targetPath"] = "/Script/Test.DataAsset";
        fixture.Fixture.Exports.Single(value => value["path"]!.GetValue<string>() == row["path"]!.GetValue<string>())["classPath"] = "/Script/Test.DataAsset";
        Assert.Throws<InvalidDataException>(() => fixture.Validate("42", fixture.Relation("default-container")));
    }

    [Theory]
    [InlineData("nonmatching-tag")]
    [InlineData("missing-tags")]
    [InlineData("invalid-tokens")]
    [InlineData("trailing-tokens")]
    [InlineData("wrong-query-structure")]
    [InlineData("wrong-tag-structure")]
    [InlineData("default-as-allowed")]
    public void AllowedMembershipRequiresTheCompleteTypedQuery(string mutation)
    {
        using var fixture = new RootFixture();
        var relation = fixture.Relation("allowed-container");
        var id = "43";
        switch (mutation)
        {
            case "nonmatching-tag": fixture.AllowedItem["values"]![0]!["value"] = "Inventory.Other"; break;
            case "missing-tags": fixture.AllowedItem["values"]!.AsArray().Clear(); break;
            case "invalid-tokens": fixture.Tokens["value"] = Convert.ToBase64String([0, 1, 1, 2, 0]); break;
            case "trailing-tokens": fixture.Tokens["value"] = Convert.ToBase64String([0, 1, 1, 1, 0, 0]); break;
            case "wrong-query-structure": fixture.Fixture.Mappings.Types["InventoryContainerSlotDataAsset"].Properties[1].MappingType.StructType = "Other"; break;
            case "wrong-tag-structure": fixture.Fixture.Mappings.Types["InventoryContainerItemDataAsset"].Properties[0].MappingType.StructType = "Other"; break;
            case "default-as-allowed": relation["containerPath"] = RootFixture.Default; id = "42"; break;
        }
        Assert.Throws<InvalidDataException>(() => fixture.Validate(id, relation));
    }

    [Fact]
    public void AnAbsentDeclaredTagContainerIsEmptyForNegativeQueries()
    {
        using var fixture = new RootFixture();
        fixture.AllowedItem["properties"]!.AsArray().RemoveAt(1);
        fixture.AllowedItem["values"]!.AsArray().Clear();
        fixture.Tokens["value"] = Convert.ToBase64String([0, 1, 3, 1, 0]);

        fixture.Validate("43", fixture.Relation("allowed-container"));
    }

    [Fact]
    public void NullRootOverrideMasksAnInheritedReference()
    {
        using var fixture = new RootFixture();
        var child = fixture.Fixture.Object("/Game/Child.Child", "InventoryTreeRootAsset", RootFixture.RootPath);
        Reference(child, "StashSlot", null);
        var relation = fixture.Relation("container-slot");
        relation["rootPath"] = child["path"]!.GetValue<string>();
        Assert.Throws<InvalidDataException>(() => fixture.Validate("41", relation));
    }

    [Fact]
    public void CompatibleInheritedRootReferencesRemainValid()
    {
        using var fixture = new RootFixture();
        var child = fixture.Fixture.Object("/Game/Child.Child", "InventoryTreeRootAsset", RootFixture.RootPath);
        var relation = fixture.Relation("container-slot");
        relation["rootPath"] = child["path"]!.GetValue<string>();
        fixture.Validate("41", relation);
    }

    [Theory]
    [InlineData("root")]
    [InlineData("slot")]
    [InlineData("container")]
    [InlineData("metadata")]
    public void ClassDefaultsCannotBecomeRootPresentationSources(string target)
    {
        using var fixture = new RootFixture();
        var row = target switch
        {
            "root" => fixture.Root,
            "slot" => fixture.Slot,
            "container" => fixture.DefaultItem,
            _ => fixture.Label
        };
        var owner = fixture.Fixture.Object("/Game/Class.Class", "Class");
        owner["references"]!.AsArray().Add(Link("/Native/ClassDefaultObject", row["path"]!.GetValue<string>(), "class-default", "hard"));
        Assert.Throws<InvalidDataException>(() => fixture.Validate("42", fixture.Relation("default-container")));
    }

    [Theory]
    [InlineData("wrong-source")]
    [InlineData("missing-evidence")]
    [InlineData("duplicate")]
    [InlineData("wrong-category")]
    public void StructuralOwnershipRequiresThisAssetsActualSource(string mutation)
    {
        using var fixture = new RootFixture();
        var relation = fixture.Relation("container-slot");
        var rows = new JsonArray(relation);
        HashSet<string> sources = [RootFixture.SlotPath];
        HashSet<string> discovered = [RootFixture.RootPath, RootFixture.SlotPath, RootFixture.LabelPath];
        switch (mutation)
        {
            case "wrong-source": sources.Clear(); break;
            case "missing-evidence": discovered.Remove(RootFixture.RootPath); break;
            case "duplicate": rows.Add(relation.DeepClone()); break;
            case "wrong-category": relation["containerType"] = "ENewInventoryContainerType::Augment"; break;
        }
        using var json = JsonDocument.Parse(rows.ToJsonString());
        Assert.Throws<InvalidDataException>(() => InventoryRootChecks.Read(json.RootElement, sources, discovered.Contains));
    }

    [Fact]
    public void CompleteRootCatalogContainsSlotDefaultAndAllowedContainers()
    {
        using var fixture = new RootFixture();
        fixture.ValidateComplete(fixture.CompleteCatalog());
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void RemovingAnEntireRootRelationshipCannotHideItsDecodedSource(int index)
    {
        using var fixture = new RootFixture();
        var catalog = fixture.CompleteCatalog();
        fixture.ValidateComplete(catalog);
        catalog[index]!["presentation"]!["inventoryRoots"]!.AsArray().Clear();
        catalog[index]!["presentation"]!["candidates"]!.AsArray().Clear();

        Assert.Throws<InvalidDataException>(() => fixture.ValidateComplete(catalog));
    }

    [Fact]
    public void EveryMetadataLabelForTheRootCategoryMustBeRetained()
    {
        using var fixture = new RootFixture();
        var label = fixture.Fixture.Object("/Game/SecondLabel.SecondLabel", "UIInventoryContainerMetaDataItem");
        Scalar(label, "ContainerType", "EnumProperty", "name", "ENewInventoryContainerType::Stash");
        var catalog = fixture.CompleteCatalog();
        foreach (var row in catalog)
        {
            var roots = row!["presentation"]!["inventoryRoots"]!.AsArray();
            var additional = roots[0]!.DeepClone();
            additional["metadataPath"] = label["path"]!.GetValue<string>();
            roots.Add(additional);
        }
        fixture.ValidateComplete(catalog);
        catalog[1]!["presentation"]!["inventoryRoots"]!.AsArray().RemoveAt(1);

        Assert.Throws<InvalidDataException>(() => fixture.ValidateComplete(catalog));
    }

    [Fact]
    public void ExplicitNullRootReferencesHaveNoRequiredRelationships()
    {
        using var fixture = new RootFixture();
        fixture.Root["references"]!.AsArray().Last()!["targetPath"] = null;
        fixture.Root["references"]!.AsArray().Last()!["isNull"] = true;
        var catalog = fixture.CompleteCatalog();
        foreach (var row in catalog) row!["presentation"]!["inventoryRoots"]!.AsArray().Clear();

        fixture.ValidateComplete(catalog);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void RootInheritanceRequiresTheActualLinksAndRespectsExplicitNull(bool explicitNull)
    {
        using var fixture = new RootFixture();
        var child = fixture.Fixture.Object("/Game/Child.Child", "InventoryTreeRootAsset", RootFixture.RootPath);
        var catalog = fixture.CompleteCatalog();
        if (explicitNull) Reference(child, "StashSlot", null);
        else
        {
            foreach (var row in catalog)
            {
                var roots = row!["presentation"]!["inventoryRoots"]!.AsArray();
                var inherited = roots[0]!.DeepClone();
                inherited["rootPath"] = child["path"]!.GetValue<string>();
                roots.Add(inherited);
            }
        }
        fixture.ValidateComplete(catalog);
        if (explicitNull) return;
        catalog[0]!["presentation"]!["inventoryRoots"]!.AsArray().RemoveAt(1);
        Assert.Throws<InvalidDataException>(() => fixture.ValidateComplete(catalog));
    }

    [Fact]
    public void DistinctContainerPathsSharingAnEffectiveIdRemainSeparateRelationships()
    {
        using var fixture = new RootFixture();
        Boolean(fixture.AllowedItem, "bOverrideItemAssetId", true);
        Number(fixture.AllowedItem, "OverrideItemAssetId", "42");
        var catalog = fixture.CompleteCatalog();
        catalog[1]!["presentation"]!["inventoryRoots"]!.AsArray().Add(fixture.Relation("allowed-container"));
        catalog.RemoveAt(2);
        fixture.ValidateComplete(catalog);
        catalog[1]!["presentation"]!["inventoryRoots"]!.AsArray().RemoveAt(1);

        Assert.Throws<InvalidDataException>(() => fixture.ValidateComplete(catalog));
    }

    private sealed class RootFixture : IDisposable
    {
        public const string RootPath = "/Game/Root.Root", SlotPath = "/Game/Slot.Slot", LabelPath = "/Game/Label.Label";
        public const string Default = "/Game/Default.Default", Allowed = "/Game/Allowed.Allowed";
        public IdentityFixture Fixture { get; } = new();
        public JsonObject Root { get; }
        public JsonObject Slot { get; }
        public JsonObject DefaultItem { get; }
        public JsonObject AllowedItem { get; }
        public JsonObject Label { get; }
        public JsonObject Tokens { get; }
        private readonly string field;

        public RootFixture(string field = "StashSlot")
        {
            this.field = field;
            Fixture.AddClass("InventoryTreeRootAsset", "DataAsset", new[] { field }.Concat(InventoryRootPolicy.Categories.Keys
                .Where(name => !name.Equals(field, StringComparison.OrdinalIgnoreCase))).Select(name => (name, "ObjectProperty")).ToArray());
            Fixture.AddClass("InventoryContainerSlotDataAsset", "ItemDataAssetBase", ("DefaultContainer", "ObjectProperty"),
                ("AllowedContainersQuery", "StructProperty"));
            Fixture.Mappings.Types["InventoryContainerSlotDataAsset"].Properties[1].MappingType.StructType = "GameplayTagQuery";
            Fixture.AddClass("InventoryContainerItemDataAsset", "ItemDataAssetBase", ("Tags", "StructProperty"));
            Fixture.Mappings.Types["InventoryContainerItemDataAsset"].Properties[0].MappingType.StructType = "GameplayTagContainer";
            Fixture.AddClass("UIInventoryContainerMetaDataItem", "Object", ("ContainerType", "EnumProperty"), ("ContainerName", "TextProperty"));
            Root = Fixture.Object(RootPath, "InventoryTreeRootAsset");
            Reference(Root, field, SlotPath);
            Slot = Item(SlotPath, "InventoryContainerSlotDataAsset", "41");
            DefaultItem = Item(Default, "InventoryContainerItemDataAsset", "42");
            AllowedItem = Item(Allowed, "InventoryContainerItemDataAsset", "43");
            var tags = Header(AllowedItem, "Tags", "StructProperty");
            Value(AllowedItem, tags + "/GameplayTags/0/TagName", "FName", "name", "Inventory.Stash.Child");
            Reference(Slot, "DefaultContainer", Default);
            var query = Header(Slot, "AllowedContainersQuery", "StructProperty");
            var dictionary = Header(Slot, "TagDictionary", "ArrayProperty", query + "/Properties");
            var tag = Header(Slot, "TagName", "NameProperty", dictionary + "/0/Properties");
            Value(Slot, tag, "NameProperty", "name", "Inventory.Stash");
            var tokens = Header(Slot, "QueryTokenStream", "ArrayProperty", query + "/Properties");
            Value(Slot, tokens, "ByteProperty[]", "binary-base64", Convert.ToBase64String([0, 1, 1, 1, 0]));
            Tokens = Slot["values"]!.AsArray().Last()!.AsObject();
            Label = Fixture.Object(LabelPath, "UIInventoryContainerMetaDataItem");
            Scalar(Label, "ContainerType", "EnumProperty", "name", InventoryRootPolicy.Categories[field]);
        }

        public JsonObject Relation(string role) => new()
        {
            ["role"] = role,
            ["containerType"] = InventoryRootPolicy.Categories[field],
            ["rootPath"] = RootPath,
            ["rootField"] = field,
            ["slotPath"] = SlotPath,
            ["metadataPath"] = LabelPath,
            ["containerPath"] = role == "container-slot" ? null : role == "default-container" ? Default : Allowed
        };

        public void Validate(string id, JsonObject relation)
        {
            var (evidence, _, context) = Fixture.Read(PresentationRelationChecks.RootFields);
            using var document = JsonDocument.Parse(new JsonArray(new JsonObject
            {
                ["id"] = id,
                ["presentation"] = new JsonObject
                {
                    ["containers"] = new JsonArray(),
                    ["visualSlots"] = new JsonArray(),
                    ["inventoryRoots"] = new JsonArray(relation.DeepClone())
                }
            }).ToJsonString());
            new PresentationRelationChecks(evidence, context).ValidateDeclared(document.RootElement);
        }

        public JsonArray CompleteCatalog() => new(
            CompleteRow("41", Relation("container-slot")), CompleteRow("42", Relation("default-container")),
            CompleteRow("43", Relation("allowed-container")));

        public void ValidateComplete(JsonArray catalog)
        {
            var (evidence, _, context) = Fixture.Read(PresentationRelationChecks.RootFields);
            using var document = JsonDocument.Parse(catalog.ToJsonString());
            new PresentationRelationChecks(evidence, context).Validate(document.RootElement);
        }

        private static JsonObject CompleteRow(string id, JsonObject relation) => new()
        {
            ["id"] = id,
            ["presentation"] = new JsonObject
            {
                ["containers"] = new JsonArray(),
                ["visualSlots"] = new JsonArray(),
                ["inventoryRoots"] = new JsonArray(relation),
                ["candidates"] = new JsonArray()
            }
        };

        private JsonObject Item(string path, string type, string id)
        {
            var persistence = Fixture.Object("/Game/Persistence" + id + ".Persistence" + id, "PersistenceDataAsset");
            Number(persistence, "AssetId", id);
            var item = Fixture.Object(path, type);
            Reference(item, "PersistenceDataAsset", persistence["path"]!.GetValue<string>());
            return item;
        }

        public void Dispose() => Fixture.Dispose();
    }
}
