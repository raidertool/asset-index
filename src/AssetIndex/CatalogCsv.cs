using System.Globalization;
using System.Text;

namespace AssetIndex;

// Convenience defaults are explicit export policy; every alternative remains in assets.json.
internal static class CatalogCsv
{
    public const string MainFile = "asset_index.csv";
    public const string LocalizationFile = "asset_localizations.csv";

    private static readonly string[] DefaultFields =
    [
        "Icon", "TinyIcon", "BigIcon", "CurrencyIcon", "EnemyIcon", "Image", "IconMaterial",
        "EmptySlotImage", "Texture", "TypeImage", "OfferImage_Thumbnail", "OfferImage_1x1", "OfferImage",
        "BigImage", "CollapsedImage", "ImageAsset", "Portrait", "UnlockImage", "PreviewImage",
        "LocationPreviewImage", "BattlepassListImage", "BattlepassCoverImage", "CoverImage", "EnemyImage",
        "CurrencyBigIcon", "OfferImage_16x9", "OfferImage_2x1", "OfferImage_9x16"
    ];

    private static readonly string[] WideFields =
    [
        "OfferImage_16x9", "OfferImage_2x1", "EnemyImage", "BigIcon", "BattlepassCoverImage",
        "LocationPreviewImage", "CoverImage", "BigImage", "CollapsedImage", "ImageAsset", "PreviewImage",
        "OfferImage", "Image", "Icon", "CurrencyBigIcon", "BattlepassListImage", "UnlockImage"
    ];

    public static IEnumerable<string[]> MainRows(IEnumerable<AssetRecord> assets)
    {
        yield return ["asset_id", "asset_name", "display_name", "description", "image", "wide_image"];
        foreach (var asset in assets.OrderBy(asset => asset.Id))
        {
            var definition = asset.Definitions.OrderBy(source => source.Class == "PersistenceDataAsset")
                .ThenBy(source => source.Path, StringComparer.Ordinal).FirstOrDefault()
                ?? throw new InvalidDataException($"Asset {asset.Id} has no technical definition.");
            var english = asset.Text.SingleOrDefault(text => text.Locale == "en");
            yield return
            [
                asset.Id.ToString(CultureInfo.InvariantCulture), definition.Name,
                EnglishDefault(english?.DisplayName, asset.Presentation.Name),
                EnglishDefault(english?.Description, asset.Presentation.Description),
                SelectImage(asset, DefaultFields, wide: false), SelectImage(asset, WideFields, wide: true)
            ];
        }
    }

    public static IEnumerable<string[]> LocalizationRows(IReadOnlyCollection<AssetRecord> assets)
    {
        yield return ["asset_id", "locale", "display_name", "description"];
        var locales = assets.SelectMany(asset => asset.Text.Select(text => text.Locale))
            .Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
        foreach (var asset in assets.OrderBy(asset => asset.Id))
        {
            var translations = asset.Text.ToDictionary(text => text.Locale, StringComparer.Ordinal);
            foreach (var locale in locales)
            {
                translations.TryGetValue(locale, out var text);
                yield return [asset.Id.ToString(CultureInfo.InvariantCulture), locale, text?.DisplayName ?? "", text?.Description ?? ""];
            }
        }
    }

    public static void Write(Stream stream, IEnumerable<string[]> rows)
    {
        using var writer = new StreamWriter(stream, new UTF8Encoding(false), leaveOpen: true) { NewLine = "\n" };
        foreach (var row in rows) writer.WriteLine(string.Join(',', row.Select(Escape)));
    }

    private static string EnglishDefault(string? translation, TextReference? source) =>
        !string.IsNullOrEmpty(translation) ? translation : source?.Source ?? "";

    private static string SelectImage(AssetRecord asset, string[] fields, bool wide)
    {
        var npcOwners = asset.Definitions.Any(source => source.Class == "NPCItemDataAsset")
            ? asset.Metadata.Where(source => source.Class == "UINPCMetaDataItem")
                .Select(source => source.Path).ToHashSet(StringComparer.Ordinal)
            : [];
        return asset.Images.Where(image => image.Status == "exported" && image.File is not null &&
                (!wide || image.Width > image.Height))
            .Select(image => (Image: image, Priority: Array.FindIndex(fields,
                field => field.Equals(image.Field, StringComparison.OrdinalIgnoreCase))))
            .Where(choice => choice.Priority >= 0)
            .OrderBy(choice => choice.Priority)
            .ThenBy(choice => !npcOwners.Contains(choice.Image.Source))
            .ThenBy(choice => choice.Image.Source, StringComparer.Ordinal)
            .ThenBy(choice => choice.Image.File, StringComparer.Ordinal)
            .Select(choice => choice.Image.File).FirstOrDefault() ?? "";
    }

    private static string Escape(string value) => value.IndexOfAny([',', '"', '\r', '\n']) >= 0
        ? '"' + value.Replace("\"", "\"\"") + '"' : value;
}
