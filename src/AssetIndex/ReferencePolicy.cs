namespace AssetIndex;

// A missing exploratory link must never supply an absent value to a catalog rule.
internal static class ReferencePolicy
{
    public static readonly IReadOnlySet<string> ProtectedFields = new HashSet<string>(
        new[]
        {
            "AssetId", "bOverrideItemAssetId", "OverrideItemAssetId", "bOverrideAssetId", "OverrideAssetId",
            "PersistenceDataAsset", "PlayerStatsRaiderTargetDataAsset", "InteractQuestDataAsset",
            "WorldQuestDataAsset", "XPEventCategoryDataAsset", "Asset",
            "Containers", "ContainerType", "DefaultContainer", "Tags", "ItemsQuery", "TypeTag",
            "CharacterCustomizationTypeTag", "AllowedContainersQuery", "ContainerName", "DisplayName"
        }.Concat(InventoryRootPolicy.Categories.Keys).Concat(TextRolePolicy.PropertyNames)
            .Concat(ImageFields.Simple).Concat(ImageFields.Typed.Keys).Concat(ImageFields.Nested.Select(field => field.Root)),
        StringComparer.OrdinalIgnoreCase);

    public static bool AllowsUnavailable(IEnumerable<string> ancestry, string field) =>
        ancestry.Contains("DataAsset", StringComparer.OrdinalIgnoreCase) && !ProtectedFields.Contains(field);

    public static string? RootPointer(string pointer)
    {
        const string prefix = "/Properties/";
        if (!pointer.StartsWith(prefix, StringComparison.Ordinal)) return null;
        var end = pointer.IndexOf('/', prefix.Length);
        return end < 0 ? pointer : pointer[..end];
    }
}
