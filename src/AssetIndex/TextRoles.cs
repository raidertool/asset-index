using CUE4Parse.MappingsProvider;
using CUE4Parse.UE4.Assets.Exports;

namespace AssetIndex;

internal sealed record TextField(string Name, string Role);

internal static class TextRoles
{
    // Roles come from these UI/definition classes, not a global match on property names.
    // Unknown classes and additional fields remain available in the raw resource export.
    private static readonly IReadOnlyDictionary<string, TextField[]> Fields = new Dictionary<string, TextField[]>
    {
        ["UIGameplayItemMetaDataItem"] = [new("ItemName", "display-name"), new("Description", "description")],
        ["UIClanBackgroundMetaDataItem"] = [new("ItemName", "display-name"), new("Description", "description")],
        ["UIClanBorderMetaDataItem"] = [new("ItemName", "display-name"), new("Description", "description")],
        ["UIClanLogoMetaDataItem"] = [new("ItemName", "display-name"), new("Description", "description")],
        ["UICharacterSkillMetaDataItem"] = [new("DisplayName", "display-name"), new("Description", "description")],
        ["UICharacterVisualSkinMetaDataItem"] = [new("DisplayName", "display-name"), new("Description", "description")],
        ["UICharacterVisualSkinPartMetaDataItem"] = [new("DisplayName", "display-name")],
        ["UICharacterVisualSkinSlotMetaDataItem"] = [new("DisplayName", "display-name")],
        ["UIEnvironmentalDamageSourceMetaDataItem"] = [new("DisplayName", "display-name")],
        ["UIStashSlotMetaDataItem"] = [new("DisplayName", "display-name"), new("Description", "description")],
        ["UINPCMetaDataItem"] = [new("DisplayName", "display-name"), new("Description", "description"), new("LocationName", "location-name")],
        ["UICurrencyMetaDataItem"] = [new("LongName", "display-name"), new("ShortName", "short-name")],
        ["UIEmoteMetaDataItem"] = [new("Text", "display-name")],
        ["UISelfieAngleMetaDataItem"] = [new("Text", "display-name")],
        ["UIEnemyMetaDataItem"] = [new("EnemyName", "display-name")],
        ["UIGameModeLocationMetaDataItem"] = [new("LocationName", "display-name"), new("LocationDescription", "description")],
        ["UIInteractQuestMetaDataItem"] = [new("InteractName", "display-name"), new("InteractDescription", "description")],
        ["UIPlayerStatsRaiderTargetMetaDataItem"] = [new("PlayerStatsRaiderTargetAllegiance", "display-name")],
        ["UIProgressionBucketMetaDataItem"] = [new("BucketName", "display-name")],
        ["UIQuestAreaMetaDataItem"] = [new("PoiName", "display-name"), new("PoiDescription", "description")],
        ["UIXPEventCategoryMetaDataItem"] = [new("XPEventCategoryName", "display-name")],
        ["UIBattlepassMetaDataItem"] = [new("BattlepassName", "display-name")],
        ["UIPurchasableOfferMetaDataItem"] = [new("OfferTitle", "title"), new("OfferDescription", "description")],
        ["UIMapConditionMetaDataItem"] = [new("Title", "title"), new("Description", "description")],
        ["UIUnlockMetaDataItem"] = [new("UnlockTitle", "title"), new("UnlockDescription", "description")],
        ["UIScoreMetaDataItem"] = [new("ScoreDescription", "description")],
        ["UISessionModifierMetaDataItem"] = [new("Description", "description")],
        ["SessionModifierDataAsset"] = [new("Description", "description")],
        ["UIInventorySlotMetaDataItem"] = [new("EmptySlotTooltipText", "tooltip")],
        ["QuestDefinition"] = [new("Title", "title"), new("Description", "description")]
    };

    public static IReadOnlyList<TextField> For(UObject source, TypeMappings? mappings)
    {
        var visited = new HashSet<string>(StringComparer.Ordinal);
        string? type = source.ExportType;
        while (type is not null && visited.Add(type))
        {
            if (Fields.TryGetValue(type, out var fields)) return fields;
            if (mappings is null || !mappings.Types.TryGetValue(type, out var definition)) break;
            type = definition.SuperType;
        }
        return [];
    }
}
