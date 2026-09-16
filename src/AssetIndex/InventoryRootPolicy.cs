using System.Collections.Frozen;

namespace AssetIndex;

// Reviewed presentation rules for the game's persistent inventory roots.
// The rule selects a UI category; its localized label still comes from game data.
internal static class InventoryRootPolicy
{
    public static readonly IReadOnlyDictionary<string, string> Categories =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["AugmentSlot"] = "ENewInventoryContainerType::Augment",
            ["StashSlot"] = "ENewInventoryContainerType::Stash",
            ["ExpeditionStashSlot"] = "ENewInventoryContainerType::Stash",
            ["BonusStashSlot"] = "ENewInventoryContainerType::Stash",
            ["SecretStashSlot"] = "ENewInventoryContainerType::Stash"
        }.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);
}
