using CUE4Parse.UE4.Assets;
using CUE4Parse.UE4.Assets.Exports;
using CUE4Parse.UE4.Assets.Objects;
using CUE4Parse.UE4.Assets.Objects.Properties;
using CUE4Parse.UE4.Assets.Readers;
using CUE4Parse.UE4.Objects.Core.i18N;
using CUE4Parse.UE4.Objects.UObject;
using CUE4Parse.UE4.Readers;
using CUE4Parse.UE4.Versions;

namespace AssetIndex.Tests;

public sealed class TextTests
{
    [Fact]
    public void ReadsNamespaceKeyAndSourceInsteadOfThePreviouslyLoadedTranslation()
    {
        var metadata = WithText("ItemName", new FText("items", "name", "Source name", "Cached French"));
        var issues = new List<ExtractionIssue>();

        var text = Text.Read(Asset(metadata), issues);

        Assert.Equal(new TextReference("items", "name", "Source name"), text.Name);
        Assert.Empty(issues);
    }

    [Fact]
    public void ReadsInheritedNameAndDirectDescription()
    {
        var parent = WithText("ItemName", new FText("items", "name", "Parent name"));
        var child = WithText("Description", new FText("items", "description", "Child description"));
        parent.Name = "Template";
        child.Name = "Instance";
        child.Template = new ResolvedLoadedObject(parent);

        var text = Text.Read(Asset(child), []);

        Assert.Equal("Parent name", text.Name?.Source);
        Assert.Equal("Child description", text.Description?.Source);
        var name = Assert.Single(text.Candidates, candidate => candidate.Field == "ItemName");
        Assert.Equal(child.GetPathName(), name.SourcePath);
        Assert.Equal(parent.GetPathName(), name.DefinedAt);
        Assert.Equal(child.GetPathName(), Assert.Single(text.Candidates,
            candidate => candidate.Field == "Description").DefinedAt);
    }

    [Fact]
    public void DirectNameOverridesInheritedName()
    {
        var child = WithText("ItemName", new FText("items", "child", "Child name"));
        child.Template = new ResolvedLoadedObject(WithText("ItemName", new FText("Parent name")));

        Assert.Equal("Child name", Text.Read(Asset(child), []).Name?.Source);
    }

    [Fact]
    public void ExplicitlyEmptyNameDoesNotRestoreTheParentName()
    {
        var child = WithText("ItemName", new FText(string.Empty));
        child.Template = new ResolvedLoadedObject(WithText("ItemName", new FText("Parent name")));

        Assert.Null(Text.Read(Asset(child), []).Name);
    }

    [Fact]
    public void CurrencyLongNameIsNotExportedAsADescription()
    {
        var metadata = WithText("LongName", new FText("Experience points"), "UICurrencyMetaDataItem");

        var text = Text.Read(Asset(metadata), []);

        Assert.Equal("Experience points", text.Name?.Source);
        Assert.Null(text.Description);
    }

    [Fact]
    public void EmptySlotTooltipProvidesADescriptionWithoutInventingAName()
    {
        var metadata = WithText("EmptySlotTooltipText",
            new FText("ST_WeaponMods", "ID_WEAPONMODS_EMPTY_SLOT_FOR_AN_UNDERBARREL_MOD", "Empty slot for an underbarrel mod."), "UIInventorySlotMetaDataItem");
        var issues = new List<ExtractionIssue>();

        var text = Text.Read(Asset(metadata), issues);

        Assert.Null(text.Name);
        Assert.Equal(new TextReference("ST_WeaponMods", "ID_WEAPONMODS_EMPTY_SLOT_FOR_AN_UNDERBARREL_MOD",
            "Empty slot for an underbarrel mod."), text.Description);
        Assert.Empty(issues);
    }

    [Theory]
    [InlineData("Description")]
    [InlineData("UnlockDescription")]
    [InlineData("ScoreDescription")]
    public void ExistingDescriptionFieldsTakePriorityOverTheSlotTooltip(string field)
    {
        var tooltip = WithText("EmptySlotTooltipText", new FText("Fallback tooltip"), "UIInventorySlotMetaDataItem");
        var type = field switch { "UnlockDescription" => "UIUnlockMetaDataItem", "ScoreDescription" => "UIScoreMetaDataItem", _ => "UIGameplayItemMetaDataItem" };
        var description = WithText(field, new FText("Preferred description"), type);
        var issues = new List<ExtractionIssue>();

        var text = Text.Read(new CatalogAsset(42, [], [tooltip, description]), issues);

        Assert.Equal("Preferred description", text.Description?.Source);
        Assert.Empty(issues);
    }

    [Fact]
    public void MissingTranslationUsesSourceOnlyForEnglish()
    {
        var reference = new TextReference("items", "name", "Source name");
        var translations = new Dictionary<string, IReadOnlyDictionary<string, string>>();

        Assert.Equal("Source name", Text.Resolve(reference, translations, "en"));
        Assert.Equal(string.Empty, Text.Resolve(reference, translations, "fr"));
    }

    [Fact]
    public void ExplicitTranslationEqualToEnglishStillCountsAsTranslated()
    {
        var reference = new TextReference("items", "name", "XP");
        var translations = new Dictionary<string, IReadOnlyDictionary<string, string>>
        {
            ["items"] = new Dictionary<string, string> { ["name"] = "XP" }
        };

        Assert.Equal("XP", Text.Resolve(reference, translations, "fr"));
    }

    [Fact]
    public void MissingKeyDoesNotUseAnUnrelatedNamespaceOrEmptyTranslationFallback()
    {
        var translations = new Dictionary<string, IReadOnlyDictionary<string, string>>
        {
            ["other"] = new Dictionary<string, string> { ["name"] = "Wrong namespace" },
            ["items"] = new Dictionary<string, string> { ["blank"] = string.Empty }
        };

        Assert.Equal(string.Empty, Text.Resolve(new("items", "name", "Source"), translations, "de"));
        Assert.Equal(string.Empty, Text.Resolve(new("items", "blank", "Source"), translations, "en"));
    }

    [Fact]
    public void UnsupportedFormattedHistoryIsReportedWithoutPublishingTheTemplate()
    {
        var metadata = WithText("ItemName", new FText(0, ETextHistoryType.NamedFormat, new FormattedHistory()));
        var issues = new List<ExtractionIssue>();

        var text = Text.Read(Asset(metadata), issues);

        Assert.Null(text.Name);
        Assert.Contains("NamedFormat", Assert.Single(issues).Message);
    }

    [Fact]
    public void QuestDefinitionTitleDoesNotRequireItemMetadata()
    {
        var definition = WithText("Title", new FText("quests", "title", "A new quest"), "QuestDefinition");
        var asset = new CatalogAsset(42, [definition], []);
        Assert.Equal("A new quest", Text.Read(asset, []).Name?.Source);
    }

    [Theory]
    [InlineData("ItemName")]
    [InlineData("Description")]
    public void DifferentKeysOnTwoDefinitionsReportBothPaths(string field)
    {
        var first = WithText(field, new FText("items", "first", "Shared source"));
        first.Name = "DA_First";
        var second = WithText(field, new FText("items", "second", "Shared source"));
        second.Name = "DA_Second";
        var issues = new List<ExtractionIssue>();

        var text = Text.Read(new CatalogAsset(42, [first, second], []), issues);

        Assert.Null(field == "ItemName" ? text.Name : text.Description);
        Assert.Equal(2, text.Candidates.Count);
        var issue = Assert.Single(issues);
        Assert.Equal("text", issue.Stage);
        Assert.Contains($"{first.GetPathName()}.{field}", issue.Message);
        Assert.Contains($"{second.GetPathName()}.{field}", issue.Message);
    }

    [Fact]
    public void IdenticalReferencesAcrossDefinitionsDoNotReportAConflict()
    {
        var first = WithText("ItemName", new FText("items", "name", "Shared name"));
        first.Name = "DA_First";
        var second = WithText("ItemName", new FText("items", "name", "Shared name"));
        second.Name = "DA_Second";
        var issues = new List<ExtractionIssue>();

        var text = Text.Read(new CatalogAsset(42, [first, second], []), issues);

        Assert.Equal(new TextReference("items", "name", "Shared name"), text.Name);
        Assert.Empty(issues);
    }

    [Fact]
    public void CurrencyFullNameIsPrimaryAndItsShortNameRemainsAnAlias()
    {
        var shortName = WithText("ShortName", new FText("XP"), "UICurrencyMetaDataItem");
        var longName = WithText("LongName", new FText("Experience points"), "UICurrencyMetaDataItem");
        var issues = new List<ExtractionIssue>();

        var text = Text.Read(new CatalogAsset(42, [], [longName, shortName]), issues);

        Assert.Equal("Experience points", text.Name?.Source);
        Assert.Contains(text.Candidates, candidate => candidate.Role == "short-name" && candidate.Reference.Source == "XP");
        Assert.Empty(issues);
    }

    [Theory]
    [InlineData("Text")]
    [InlineData("Title")]
    public void GenericFieldsWithoutAProvenClassRoleAreNotNames(string field)
    {
        var source = WithText(field, new FText("Unrelated widget label"));
        Assert.Null(Text.Read(Asset(source), []).Name);
    }

    [Fact]
    public void EmoteTextHasAnExplicitNameRole()
    {
        var source = WithText("Text", new FText("Angry"), "UIEmoteMetaDataItem");
        Assert.Equal("Angry", Text.Read(Asset(source), []).Name?.Source);
    }

    [Fact]
    public void NpcLocationLabelDoesNotBecomeTheNpcName()
    {
        var source = WithText("LocationName", new FText("Grenades & Gadgets"), "UINPCMetaDataItem");
        var text = Text.Read(Asset(source), []);
        Assert.Null(text.Name);
        Assert.Equal("location-name", Assert.Single(text.Candidates).Role);
    }

    [Fact]
    public void UiModifierDescriptionWinsAndDefinitionTextKeepsItsProvenance()
    {
        var definition = WithText("Description", new FText("Definition description"), "SessionModifierDataAsset");
        definition.Name = "Definition";
        var metadata = WithText("Description", new FText("UI description"), "UISessionModifierMetaDataItem");
        var issues = new List<ExtractionIssue>();
        var text = Text.Read(new CatalogAsset(42, [definition], [metadata]), issues);

        Assert.Equal("UI description", text.Description?.Source);
        Assert.Contains(text.Candidates, candidate => candidate.SourceKind == "definition"
            && candidate.SourcePath == definition.GetPathName() && candidate.Reference.Source == "Definition description");
        Assert.Empty(issues);
    }

    [Fact]
    public void SerializedNoneHistoryKeepsItsInvariantStringInEveryLocale()
    {
        // Flags=0, history=None, present=true, FString="XP".
        byte[] bytes = [0, 0, 0, 0, 255, 1, 0, 0, 0, 3, 0, 0, 0, 88, 80, 0];
        using var archive = new FAssetArchive(new FByteArchive("invariant-text", bytes,
            new VersionContainer(EGame.GAME_ArcRaiders)), null);
        var metadata = WithText("ItemName", new FText(archive));
        var issues = new List<ExtractionIssue>();

        var reference = Text.Read(Asset(metadata), issues).Name;

        Assert.Equal(new TextReference("", "", "XP", CultureInvariant: true), reference);
        Assert.Equal("XP", Text.Resolve(reference, new Dictionary<string, IReadOnlyDictionary<string, string>>(), "fr"));
        Assert.Empty(issues);
    }

    [Fact]
    public void ExplicitInvariantFlagIgnoresLocalizationAndCachedText()
    {
        var metadata = WithText("ItemName", new FText((uint)ETextFlag.CultureInvariant, ETextHistoryType.Base,
            new FTextHistory.Base("items", "name", "XP", "Cached text")));
        var translations = new Dictionary<string, IReadOnlyDictionary<string, string>>
        {
            ["items"] = new Dictionary<string, string> { ["name"] = "Wrong translation" }
        };

        var reference = Text.Read(Asset(metadata), []).Name;

        Assert.Equal(new TextReference("items", "name", "XP", CultureInvariant: true), reference);
        Assert.Equal("XP", Text.Resolve(reference, translations, "fr"));
    }

    [Theory]
    [InlineData((ETextFlag)0)]
    [InlineData(ETextFlag.InitializedFromString)]
    public void KeylessBaseHistoryIsNotAssumedToBeInvariant(ETextFlag flags)
    {
        var metadata = WithText("ItemName", new FText((uint)flags, ETextHistoryType.Base,
            new FTextHistory.Base("", "", "Source name")));
        var translations = new Dictionary<string, IReadOnlyDictionary<string, string>>();

        var reference = Text.Read(Asset(metadata), []).Name;

        Assert.Equal(new TextReference("", "", "Source name"), reference);
        Assert.Equal("Source name", Text.Resolve(reference, translations, "en"));
        Assert.Equal(string.Empty, Text.Resolve(reference, translations, "fr"));
    }

    private static CatalogAsset Asset(UObject metadata) =>
        new(1, [new UObject { Name = "DA_Test" }], [metadata]);

    private static UObject WithText(string field, FText text, string type = "UIGameplayItemMetaDataItem")
    {
        var value = new UObject { Name = "UI_Test", Class = new ResolvedLoadedObject(new UObject { Name = type }) };
        value.Properties.Add(new FPropertyTag(new FName("TextProperty"), new TextProperty(text))
        {
            Name = new FName(field)
        });
        return value;
    }

    private sealed class FormattedHistory : FTextHistory
    {
        public override string Text => "Unlock {Item}";
    }
}
