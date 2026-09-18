using System.Text.Json.Nodes;
using static PublishSnapshot.Tests.IdentityFixture;

namespace PublishSnapshot.Tests;

public sealed class IdentityCompletenessTests
{
    [Fact]
    public void TechnicalNameMustMatchTheNativeExportName()
    {
        using var fixture = new IdentityFixture();
        var source = fixture.Object("/Game/Outer.Outer:NativeName", "PersistenceDataAsset");
        Number(source, "AssetId", "42");
        var catalog = Catalog("42", [source]);
        var context = fixture.Read().Context;
        Validate(context, catalog);
        catalog["definitions"]![0]!["name"] = "FilenameGuess";

        var error = Assert.Throws<InvalidDataException>(() => Validate(context, catalog));

        Assert.Contains("source name differs from its decoded object name", error.Message);
    }

    [Fact]
    public void EveryDecodedIdentityRequiresItsCatalogRow()
    {
        using var fixture = new IdentityFixture();
        var first = fixture.Object("/Game/First.First", "PersistenceDataAsset");
        var second = fixture.Object("/Game/Second.Second", "PersistenceDataAsset");
        Number(first, "AssetId", "42");
        Number(second, "AssetId", "43");
        var context = fixture.Read().Context;
        Validate(context, Catalog("42", [first]), Catalog("43", [second]));

        var error = Assert.Throws<InvalidDataException>(() => Validate(context, Catalog("42", [first])));

        Assert.Contains("omits decoded definition identity 43: /Game/Second.Second", error.Message);
    }

    [Fact]
    public void AllDefinitionsSharingAnIdentityAreRequired()
    {
        using var fixture = new IdentityFixture();
        var persistence = fixture.Object("/Game/Identity.Identity", "PersistenceDataAsset");
        Number(persistence, "AssetId", "42");
        var item = fixture.Object("/Game/Item.Item", "ItemDataAssetBase");
        Reference(item, "PersistenceDataAsset", "/Game/Identity.Identity");
        var context = fixture.Read().Context;
        Validate(context, Catalog("42", [persistence, item]));

        var error = Assert.Throws<InvalidDataException>(() => Validate(context, Catalog("42", [persistence])));

        Assert.Contains("omits decoded definition identity 42: /Game/Item.Item", error.Message);
    }

    [Fact]
    public void OmittingNpcMetadataCannotHideItsHigherPriorityAuthoredText()
    {
        using var fixture = new IdentityFixture();
        fixture.AddClass("NPCItemDataAsset", "ItemDataAssetBase");
        fixture.AddClass("UINPCMetaDataItem", "UIMetaDataItem", ("DisplayName", "TextProperty"));
        fixture.AddClass("UIGameplayItemMetaDataItem", "UIMetaDataItem", ("ItemName", "TextProperty"));
        var persistence = fixture.Object("/Game/Identity.Identity", "PersistenceDataAsset");
        Number(persistence, "AssetId", "42");
        var npc = fixture.Object("/Game/Npc.Npc", "NPCItemDataAsset");
        Reference(npc, "PersistenceDataAsset", "/Game/Identity.Identity");
        var generic = Metadata("/Game/Generic.Generic", "UIGameplayItemMetaDataItem", "ItemName", "Juanito");
        var specific = Metadata("/Game/Specific.Specific", "UINPCMetaDataItem", "DisplayName", "Ermal");
        var context = fixture.Read("ItemName", "DisplayName").Context;
        Validate(context, Catalog("42", [npc, persistence], [generic, specific]));

        var error = Assert.Throws<InvalidDataException>(() => Validate(context, Catalog("42", [npc, persistence], [generic])));

        Assert.Contains("omits decoded metadata identity 42: /Game/Specific.Specific", error.Message);

        JsonObject Metadata(string path, string type, string field, string text)
        {
            var source = fixture.Object(path, type);
            Reference(source, "PersistenceDataAsset", "/Game/Identity.Identity");
            source["texts"]!.AsArray().Add(new JsonObject
            {
                ["pointer"] = Header(source, field, "TextProperty"),
                ["flags"] = 2,
                ["history"] = "None",
                ["namespace"] = null,
                ["key"] = null,
                ["source"] = text,
                ["tableId"] = null
            });
            return source;
        }
    }

    [Fact]
    public void AnonymousAndLocalOnlyObjectsDoNotBecomeCatalogRows()
    {
        using var fixture = new IdentityFixture();
        fixture.Mappings.Types["UIMetaDataItem"].Properties.Clear();
        fixture.AddClass("AnonymousMetadata", "UIMetaDataItem");
        var persistence = fixture.Object("/Game/Identity.Identity", "PersistenceDataAsset");
        Number(persistence, "AssetId", "42");
        var local = fixture.Object("/Game/Local.Local", "ItemDataAssetBase");
        Boolean(local, "bOverrideItemAssetId", false);
        Reference(local, "PersistenceDataAsset", null);
        fixture.Object("/Game/Anonymous.Anonymous", "AnonymousMetadata");

        Validate(fixture.Read().Context, Catalog("42", [persistence]));
    }

    [Fact]
    public void ExplicitClassDefaultIsExcludedEvenIfItHasAnIdentity()
    {
        using var fixture = new IdentityFixture();
        var persistence = fixture.Object("/Game/Identity.Identity", "PersistenceDataAsset");
        Number(persistence, "AssetId", "42");
        var declaration = fixture.Object("/Game/Type.Type", "Class");
        declaration["references"]!.AsArray().Add(Link("/Native/ClassDefaultObject", "/Game/Archetype.Archetype", "class-default", "hard"));
        var cdo = fixture.Object("/Game/Archetype.Archetype", "PersistenceDataAsset");
        Number(cdo, "AssetId", "43");

        Validate(fixture.Read().Context, Catalog("42", [persistence]));
    }

    [Fact]
    public void MissingRequiredMetadataIdentityCannotBeHiddenByOmission()
    {
        using var fixture = new IdentityFixture();
        var persistence = fixture.Object("/Game/Identity.Identity", "PersistenceDataAsset");
        Number(persistence, "AssetId", "42");
        fixture.Object("/Game/Unresolved.Unresolved", "UIMetaDataItem");

        var error = Assert.Throws<InvalidDataException>(() => Validate(fixture.Read().Context, Catalog("42", [persistence])));

        Assert.Contains("Metadata has no resolvable typed identity", error.Message);
    }
}

public sealed partial class PublisherTests
{
    [Fact]
    public void OmittingMetadataAndEveryDependentPresentationValueCannotPassExport()
    {
        ChangeJson("assets.json", rows =>
        {
            var asset = rows[0]!;
            asset["metadata"]!.AsArray().Clear();
            asset["images"]!.AsArray().Clear();
            asset["presentation"]!["candidates"]!.AsArray().Clear();
            asset["presentation"]!["name"] = null;
            asset["presentation"]!["description"] = null;
            asset["text"]![0]!["displayName"] = "";
            asset["text"]![0]!["description"] = "";
        });
        ChangeJson("coverage.json", report =>
        {
            report["englishNames"] = 0;
            report["descriptions"] = 0;
            report["images"] = 0;
        });
        var output = Path.Combine(root, "incomplete-catalog-export");

        var error = Assert.Throws<InvalidDataException>(() => Publisher.Export(preview, output, NextExtractor, "456"));

        Assert.Contains("Catalog omits decoded metadata identity 42", error.Message);
        Assert.False(Path.Exists(output));
    }
}
