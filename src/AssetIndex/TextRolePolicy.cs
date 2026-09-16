namespace AssetIndex;

internal sealed record TextField(string Name, string Role);

internal static class TextRolePolicy
{
    // Roles come from these UI/definition classes, not a global match on property names.
    // Unknown classes and additional fields remain available in the raw resource export.
    private static readonly IReadOnlyDictionary<string, TextField[]> Fields = new Dictionary<string, TextField[]>(StringComparer.OrdinalIgnoreCase)
    {
        ["UIGameplayItemMetaDataItem"] = [new("ItemName", "display-name"), new("Description", "description")],
        ["UIClanBackgroundMetaDataItem"] = [new("ItemName", "display-name"), new("Description", "description")],
        ["UIClanBorderMetaDataItem"] = [new("ItemName", "display-name"), new("Description", "description")],
        ["UIClanLogoMetaDataItem"] = [new("ItemName", "display-name"), new("Description", "description")],
        ["UICharacterSkillMetaDataItem"] = [new("DisplayName", "display-name"), new("Description", "description")],
        ["UICharacterVisualSkinMetaDataItem"] = [new("DisplayName", "display-name"), new("Description", "description")],
        ["UICharacterVisualSkinPartMetaDataItem"] = [new("DisplayName", "display-name")],
        ["UICharacterVisualSkinSlotMetaDataItem"] = [new("DisplayName", "display-name")],
        ["UICharacterCustomizationQuickNavTabMetaDataItem"] = [new("DisplayName", "display-name")],
        ["UIEnvironmentalDamageSourceMetaDataItem"] = [new("DisplayName", "display-name")],
        ["UIStashSlotMetaDataItem"] = [new("DisplayName", "display-name"), new("Description", "description"), new("EffectFormatText", "effect-format")],
        ["UINPCMetaDataItem"] =
        [
            new("DisplayName", "display-name"), new("Description", "description"), new("LocationName", "location-name"),
            new("ObscuredDisplayName", "obscured-name"), new("ObscuredDescription", "obscured-description"),
            new("ObscuredLocationName", "obscured-location-name")
        ],
        ["UICurrencyMetaDataItem"] = [new("LongName", "display-name"), new("ShortName", "short-name")],
        ["UIEmoteMetaDataItem"] = [new("Text", "display-name")],
        ["UISelfieAngleMetaDataItem"] = [new("Text", "display-name")],
        ["UIEnemyMetaDataItem"] = [new("EnemyName", "display-name")],
        ["UIGameModeLocationMetaDataItem"] =
        [
            new("LocationName", "display-name"), new("LocationDescription", "description"),
            new("LocationNameShort", "short-name"), new("LocationAreaName", "area-name")
        ],
        ["UIInteractQuestMetaDataItem"] = [new("InteractName", "display-name"), new("InteractDescription", "description")],
        ["UIPlayerStatsRaiderTargetMetaDataItem"] = [new("PlayerStatsRaiderTargetAllegiance", "display-name")],
        ["UIProgressionBucketMetaDataItem"] = [new("BucketName", "display-name")],
        ["UIQuestAreaMetaDataItem"] = [new("PoiName", "display-name"), new("PoiDescription", "description")],
        ["UIXPEventCategoryMetaDataItem"] = [new("XPEventCategoryName", "display-name")],
        ["UIBattlepassMetaDataItem"] = [new("BattlepassName", "display-name")],
        ["UIPurchasableOfferMetaDataItem"] = [new("OfferTitle", "title"), new("OfferDescription", "description")],
        ["UIMapConditionMetaDataItem"] = [new("Title", "title"), new("Description", "description")],
        ["UIUnlockMetaDataItem"] =
        [new("UnlockTitle", "title"), new("UnlockDescription", "unlock-description"), new("NavigationText", "navigation-text")],
        ["UIScoreMetaDataItem"] = [new("ScoreDescription", "description")],
        ["UISessionModifierMetaDataItem"] = [new("Description", "description")],
        ["SessionModifierDataAsset"] = [new("Description", "description")],
        ["UIInventorySlotMetaDataItem"] = [new("EmptySlotTooltipText", "tooltip")],
        ["QuestDefinition"] = [new("Title", "title"), new("Description", "description")]
    };

    public static IEnumerable<string> PropertyNames => Fields.Values.SelectMany(fields => fields).Select(item => item.Name).Distinct(StringComparer.OrdinalIgnoreCase);

    public static IReadOnlyList<TextField> For(IEnumerable<string> nativeAncestry)
    {
        foreach (var type in nativeAncestry)
            if (Fields.TryGetValue(type, out var fields)) return fields;
        return [];
    }
}
