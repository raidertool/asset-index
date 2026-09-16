namespace AssetIndex;

internal sealed record NestedImageField(string Owner, string Root, string Structure, string Leaf, bool IsArray);

// Shared field policy for extraction and validation of decoded image references.
internal static class ImageFields
{
    public static readonly string[] Simple =
    [
        "Icon", "BigIcon", "TinyIcon", "Image", "CurrencyIcon", "CurrencyBigIcon", "EnemyIcon", "EnemyImage",
        "OfferImage", "OfferImage_1x1", "OfferImage_2x1", "OfferImage_9x16", "OfferImage_16x9", "OfferImage_Thumbnail",
        "BigImage", "CollapsedImage", "BattlepassListImage", "BattlepassCoverImage", "LocationPreviewImage",
        "Portrait", "ImageAsset", "UnlockImage", "PreviewImage", "IconMaterial", "EmptySlotImage",
        "ModifierIcon", "ObscuredPreviewImage", "OptionalLocationIcon", "UnlockVideoPreviewImage"
    ];

    public static readonly IReadOnlyDictionary<string, string> Typed = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["Texture"] = "UIClanLogoMetaDataItem",
        ["TypeImage"] = "UIClanCustomizationMetaDataItem",
        ["CoverImage"] = "UIEnvironmentalDamageSourceMetaDataItem"
    };

    public static readonly NestedImageField[] Nested =
    [
        new("UIMapAreaInfoMetaDataItem", "MapAreas", "MapAreaInfo", "HeaderImage", true),
        new("UIMapWidgetMetaDataItem", "MapWidgetSettings", "MapWidgetLevelSettings", "MapTexture", false)
    ];
}
