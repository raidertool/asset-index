using CUE4Parse.MappingsProvider;
using CUE4Parse.MappingsProvider.Usmap;
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
    private static readonly Lazy<TypeMappings> Mappings = new(() =>
        new FileUsmapTypeMappingsProvider(Path.Combine(AppContext.BaseDirectory, "mappings", "ArcRaiders.usmap")).MappingsForGame!);

    [Theory]
    [InlineData("ItemName")]
    [InlineData("itemname")]
    public void ReadsNamespaceKeyAndSourceInsteadOfThePreviouslyLoadedTranslation(string field)
    {
        var metadata = WithText(field, new FText("items", "name", "Source name", "Cached French"));
        var issues = new List<ExtractionIssue>();

        var text = Text.Read(Asset(metadata, issues), issues);

        Assert.Equal(new TextReference("items", "name", "Source name"), text.Name);
        Assert.Equal(field, Assert.Single(text.Candidates).Field);
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
        var child = WithText("itemname", new FText(string.Empty));
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

        var text = Text.Read(Asset(metadata, issues), issues);

        Assert.Null(text.Name);
        Assert.Equal(new TextReference("ST_WeaponMods", "ID_WEAPONMODS_EMPTY_SLOT_FOR_AN_UNDERBARREL_MOD",
            "Empty slot for an underbarrel mod."), text.Description);
        Assert.Empty(issues);
    }

    [Theory]
    [InlineData("Description")]
    [InlineData("ScoreDescription")]
    public void ExistingDescriptionFieldsTakePriorityOverTheSlotTooltip(string field)
    {
        var tooltip = WithText("EmptySlotTooltipText", new FText("Fallback tooltip"), "UIInventorySlotMetaDataItem");
        var type = field == "ScoreDescription" ? "UIScoreMetaDataItem" : "UIGameplayItemMetaDataItem";
        var description = WithText(field, new FText("Preferred description"), type);
        var issues = new List<ExtractionIssue>();

        var text = Text.Read(Asset([], [tooltip, description], issues), issues);

        Assert.Equal("Preferred description", text.Description?.Source);
        Assert.Empty(issues);
    }

    [Theory]
    [InlineData("Description", "UIGameplayItemMetaDataItem")]
    [InlineData("EmptySlotTooltipText", "UIInventorySlotMetaDataItem")]
    public void UnlockInstructionsDoNotCompeteWithAnAssetDescription(string field, string type)
    {
        var description = WithText(field, new FText("Medical Lab description"), type);
        var unlock = WithText("UnlockDescription", new FText("Requires a Medical Lab"), "UIUnlockMetaDataItem");
        unlock.Name = "Unlock";
        var issues = new List<ExtractionIssue>();

        var text = Text.Read(Asset([], [unlock, description], issues), issues);

        Assert.Equal("Medical Lab description", text.Description?.Source);
        Assert.Contains(text.Candidates, candidate => candidate.Role == "unlock-description"
            && candidate.Reference.Source == "Requires a Medical Lab");
        Assert.Empty(issues);
    }

    [Fact]
    public void UnlockInstructionsAloneRemainContextualText()
    {
        var unlock = WithText("UnlockDescription", new FText("Rounds played: {0}/{1}"), "UIUnlockMetaDataItem");
        var issues = new List<ExtractionIssue>();

        var text = Text.Read(Asset(unlock, issues), issues);

        Assert.Null(text.Description);
        Assert.Equal("unlock-description", Assert.Single(text.Candidates).Role);
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

        var text = Text.Read(Asset(metadata, issues), issues);

        Assert.Null(text.Name);
        Assert.Contains("NamedFormat", Assert.Single(issues).Message);
        Assert.Empty(text.Notices);
    }

    [Fact]
    public void QuestDefinitionTitleDoesNotRequireItemMetadata()
    {
        var definition = WithText("Title", new FText("quests", "title", "A new quest"), "QuestDefinition");
        var asset = Asset([definition], [], []);
        Assert.Equal("A new quest", Text.Read(asset, []).Name?.Source);
    }

    [Theory]
    [InlineData("ItemName")]
    [InlineData("Description")]
    public void DifferentKeysOnTwoDefinitionsPreserveCandidatesAndNoticeBothPaths(string field)
    {
        var first = WithText(field, new FText("items", "first", "Shared source"));
        first.Name = "DA_First";
        var second = WithText(field, new FText("items", "second", "Shared source"));
        second.Name = "DA_Second";
        var issues = new List<ExtractionIssue>();

        var text = Text.Read(Asset([first, second], [], issues), issues);

        Assert.Null(field == "ItemName" ? text.Name : text.Description);
        Assert.Equal(2, text.Candidates.Count);
        Assert.Empty(issues);
        var notice = Assert.Single(text.Notices);
        Assert.Equal("text", notice.Stage);
        Assert.Contains($"{first.GetPathName()}.{field}", notice.Message);
        Assert.Contains($"{second.GetPathName()}.{field}", notice.Message);
        Assert.Equal(new[] { "first", "second" }, text.Candidates.Select(candidate => candidate.Reference.Key));
    }

    [Fact]
    public void ConflictingNpcNamesKeepTheirProvenanceWithoutChoosingADefinitionFallback()
    {
        var gameplay = WithText("ItemName", new FText("ST_NPC", "ID_JUANITO_NAME", "Juanito"));
        gameplay.Name = "Gameplay";
        var npc = WithText("DisplayName", new FText("ST_Trades", "ID_TRADES_JUANITO_NAME_ALT3", "Ermal"), "UINPCMetaDataItem");
        npc.Name = "Npc";
        var definition = WithText("Title", new FText("Fallback name"), "QuestDefinition");
        definition.Name = "Definition";
        var issues = new List<ExtractionIssue>();

        var text = Text.Read(Asset([definition], [gameplay, npc], issues), issues);

        Assert.Null(text.Name);
        Assert.Empty(issues);
        Assert.Single(text.Notices);
        Assert.Equal(3, text.Candidates.Count);
        Assert.Contains(text.Candidates, candidate => candidate.SourcePath == "Gameplay" && candidate.DefinedAt == "Gameplay"
            && candidate.Field == "ItemName" && candidate.Reference == new TextReference("ST_NPC", "ID_JUANITO_NAME", "Juanito"));
        Assert.Contains(text.Candidates, candidate => candidate.SourcePath == "Npc" && candidate.DefinedAt == "Npc"
            && candidate.Field == "DisplayName" && candidate.Reference == new TextReference("ST_Trades", "ID_TRADES_JUANITO_NAME_ALT3", "Ermal"));
    }

    [Fact]
    public void NpcDefinitionUsesSpecificUiNameAndRetainsGenericItemName()
    {
        var gameplay = WithText("ItemName", new FText("Juanito"));
        var npc = WithText("DisplayName", new FText("Ermal"), "UINPCMetaDataItem");
        var text = Text.Read(Asset([Definition("NPCItemDataAsset")], [gameplay, npc], []), []);

        Assert.Equal("Ermal", text.Name?.Source);
        Assert.Equal(2, text.Candidates.Count);
        Assert.Contains(text.Candidates, candidate => candidate.Reference.Source == "Juanito");
        Assert.Empty(text.Notices);
    }

    [Fact]
    public void NpcSpecificNameRetainsTemplateProvenance()
    {
        var parent = WithText("DisplayName", new FText("Ermal"), "UINPCMetaDataItem");
        parent.Name = "Template";
        var npc = Definition("UINPCMetaDataItem");
        npc.Template = new ResolvedLoadedObject(parent);
        var text = Text.Read(Asset([Definition("NPCItemDataAsset")], [npc], []), []);

        Assert.Equal("Ermal", text.Name?.Source);
        Assert.Equal(parent.GetPathName(), Assert.Single(text.Candidates).DefinedAt);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void EmptyOrConflictingNpcSpecificNamesDoNotRestoreTheGenericName(bool conflict)
    {
        var gameplay = WithText("ItemName", new FText("Generic name"));
        var npc = WithText("DisplayName", new FText(conflict ? "First name" : ""), "UINPCMetaDataItem");
        var second = WithText("DisplayName", new FText("Second name"), "UINPCMetaDataItem");
        second.Name = "Other";
        var metadata = conflict ? new[] { gameplay, npc, second } : [gameplay, npc];

        var text = Text.Read(Asset([Definition("NPCItemDataAsset")], metadata, []), []);

        Assert.Null(text.Name);
        Assert.Equal(conflict ? 1 : 0, text.Notices.Count);
    }

    [Fact]
    public void ModifierDescriptionAlsoProvidesItsLabelWithOriginalProvenance()
    {
        var parent = WithText("Description", new FText("XP Boost 20%"), "SessionModifierDataAsset");
        parent.Name = "Template";
        var modifier = Definition("SessionModifierDataAsset");
        modifier.Template = new ResolvedLoadedObject(parent);

        var text = Text.Read(Asset([modifier], [], []), []);

        Assert.Equal(text.Description, text.Name);
        Assert.Equal("XP Boost 20%", text.Name?.Source);
        var candidate = Assert.Single(text.Candidates);
        Assert.Equal("description", candidate.Role);
        Assert.Equal("Description", candidate.Field);
        Assert.Equal(parent.GetPathName(), candidate.DefinedAt);
    }

    [Theory]
    [InlineData("Name")]
    [InlineData("")]
    public void ModifierExplicitNameOrEmptyNameDoesNotFallBackToDescription(string name)
    {
        var modifier = WithText("Description", new FText("XP Boost 20%"), "SessionModifierDataAsset");
        var metadata = WithText("ItemName", new FText(name));

        var text = Text.Read(Asset([modifier], [metadata], []), []);

        Assert.Equal(name.Length == 0 ? null : name, text.Name?.Source);
    }

    [Theory]
    [InlineData("ItemName", "UIGameplayItemMetaDataItem")]
    [InlineData("Description", "UISessionModifierMetaDataItem")]
    public void ModifierDoesNotBypassConflictingNamesOrDescriptions(string field, string type)
    {
        var modifier = WithText("Description", new FText("Definition fallback"), "SessionModifierDataAsset");
        var first = WithText(field, new FText("First"), type);
        var second = WithText(field, new FText("Second"), type);
        second.Name = "Other";

        var text = Text.Read(Asset([modifier], [first, second], []), []);

        Assert.Null(text.Name);
        Assert.Single(text.Notices);
    }

    [Theory]
    [InlineData("QuestDefinition", "Description", "UISessionModifierMetaDataItem")]
    [InlineData("SessionModifierDataAsset", "Description", "UIGameplayItemMetaDataItem")]
    [InlineData("SessionModifierDataAsset", "EmptySlotTooltipText", "UIInventorySlotMetaDataItem")]
    public void OtherDefinitionsAndUnrelatedDescriptionsDoNotGainModifierLabels(string definition, string field, string type)
    {
        var metadata = WithText(field, new FText("Unrelated description"), type);
        var text = Text.Read(Asset([Definition(definition)], [metadata], []), []);

        Assert.Null(text.Name);
        Assert.Equal("Unrelated description", text.Description?.Source);
    }

    [Fact]
    public void EmptyPrimaryMetadataDoesNotSelectALowerRoleOrDefinition()
    {
        var metadata = WithText("LongName", new FText(string.Empty), "UICurrencyMetaDataItem");
        var shortName = WithText("ShortName", new FText("XP"), "UICurrencyMetaDataItem");
        shortName.Name = "Short";
        var definition = WithText("Title", new FText("Definition name"), "QuestDefinition");
        var issues = new List<ExtractionIssue>();

        var text = Text.Read(Asset([definition], [metadata, shortName], issues), issues);

        Assert.Null(text.Name);
        Assert.Empty(issues);
        Assert.Empty(text.Notices);
        Assert.Equal(3, text.Candidates.Count);
    }

    [Fact]
    public void InvalidTextPropertyRemainsAnErrorRatherThanASelectionNotice()
    {
        var metadata = WithText("ItemName", new FText("Placeholder"));
        metadata.Properties[0] = new FPropertyTag(new FName("StrProperty"), new StrProperty("Not FText"))
        {
            Name = new FName("ItemName")
        };
        var issues = new List<ExtractionIssue>();

        var text = Text.Read(Asset(metadata, issues), issues);

        Assert.Null(text.Name);
        Assert.Empty(text.Candidates);
        Assert.Empty(text.Notices);
        Assert.Equal("text", Assert.Single(issues).Stage);
    }

    [Fact]
    public void IdenticalReferencesAcrossDefinitionsDoNotReportAConflict()
    {
        var first = WithText("ItemName", new FText("items", "name", "Shared name"));
        first.Name = "DA_First";
        var second = WithText("ItemName", new FText("items", "name", "Shared name"));
        second.Name = "DA_Second";
        var issues = new List<ExtractionIssue>();

        var text = Text.Read(Asset([first, second], [], issues), issues);

        Assert.Equal(new TextReference("items", "name", "Shared name"), text.Name);
        Assert.Empty(issues);
        Assert.Empty(text.Notices);
    }

    [Fact]
    public void CurrencyFullNameIsPrimaryAndItsShortNameRemainsAnAlias()
    {
        var shortName = WithText("ShortName", new FText("XP"), "UICurrencyMetaDataItem");
        var longName = WithText("LongName", new FText("Experience points"), "UICurrencyMetaDataItem");
        var issues = new List<ExtractionIssue>();

        var text = Text.Read(Asset([], [longName, shortName], issues), issues);

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

    [Theory]
    [InlineData("UIUnlockMetaDataItem", "NavigationText", "navigation-text")]
    [InlineData("UIStashSlotMetaDataItem", "EffectFormatText", "effect-format")]
    [InlineData("UIGameModeLocationMetaDataItem", "LocationAreaName", "area-name")]
    [InlineData("UINPCMetaDataItem", "ObscuredDisplayName", "obscured-name")]
    [InlineData("UINPCMetaDataItem", "ObscuredDescription", "obscured-description")]
    [InlineData("UINPCMetaDataItem", "ObscuredLocationName", "obscured-location-name")]
    public void ContextualLabelsKeepTheirPurposeWithoutBecomingPrimaryText(string type, string field, string role)
    {
        var source = WithText(field, new FText("Contextual label"), type);
        var issues = new List<ExtractionIssue>();

        var text = Text.Read(Asset(source, issues), issues);

        Assert.Null(text.Name);
        Assert.Null(text.Description);
        Assert.Equal(role, Assert.Single(text.Candidates).Role);
        Assert.Empty(issues);
    }

    [Fact]
    public void LocationShortNameIsAFallbackAndRetainsItsOwnRole()
    {
        var source = WithText("LocationNameShort", new FText("Short location"), "UIGameModeLocationMetaDataItem");
        var text = Text.Read(Asset(source), []);

        Assert.Equal("Short location", text.Name?.Source);
        Assert.Equal("short-name", Assert.Single(text.Candidates).Role);
    }

    [Fact]
    public void UiModifierDescriptionWinsAndDefinitionTextKeepsItsProvenance()
    {
        var definition = WithText("Description", new FText("Definition description"), "SessionModifierDataAsset");
        definition.Name = "Definition";
        var metadata = WithText("Description", new FText("UI description"), "UISessionModifierMetaDataItem");
        var issues = new List<ExtractionIssue>();
        var text = Text.Read(Asset([definition], [metadata], issues), issues);

        Assert.Equal("UI description", text.Description?.Source);
        Assert.Equal(text.Description, text.Name);
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

        var reference = Text.Read(Asset(metadata, issues), issues).Name;

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

    private static CatalogAsset Asset(UObject metadata, ICollection<ExtractionIssue>? issues = null) =>
        Asset([], [metadata], issues ?? []);

    private static UObject Definition(string type) =>
        new() { Name = "Definition", Class = new ResolvedLoadedObject(new UScriptClass(type)) };

    private static CatalogAsset Asset(UObject[] definitions, UObject[] metadata, ICollection<ExtractionIssue> issues)
    {
        CatalogSource Capture(UObject source) => new(new(source.Name, source.ExportType, source.GetPathName()),
            Text.Capture(source, Mappings.Value, issues), []);
        return new(42, definitions.Select(Capture).ToArray(), metadata.Select(Capture).ToArray());
    }

    private static UObject WithText(string field, FText text, string type = "UIGameplayItemMetaDataItem")
    {
        var value = new UObject { Name = "UI_Test", Class = new ResolvedLoadedObject(new UScriptClass(type)) };
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
