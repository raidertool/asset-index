using CUE4Parse.FileProvider;
using CUE4Parse.UE4.Assets.Exports;
using CUE4Parse.UE4.Assets.Exports.Internationalization;
using CUE4Parse.UE4.Objects.Core.i18N;

namespace AssetIndex;

internal sealed record TextReference(string Namespace, string Key, string Source, bool CultureInvariant = false);
internal sealed record AssetText(long AssetId, TextReference? Name, TextReference? Description);
internal sealed record LocalizedText(long AssetId, string Locale, string DisplayName, string Description);

internal static class Text
{
    // UI fields verified against the committed ARC mappings. LongName is a name, not a description.
    private static readonly string[] NameFields =
    [
        "ItemName", "DisplayName", "ShortName", "LongName", "Text", "OfferTitle", "EnemyName",
        "Title", "LocationName", "PlayerStatsRaiderTargetAllegiance", "InteractName", "PoiName", "XPEventCategoryName",
        "BattlepassName", "BucketName", "ViewName", "UnlockTitle"
    ];
    private static readonly string[] DescriptionFields =
        ["Description", "OfferDescription", "LocationDescription", "InteractDescription", "PoiDescription", "UnlockDescription", "ScoreDescription"];

    public static AssetText Read(CatalogAsset asset, ICollection<ExtractionIssue> issues)
    {
        var sources = asset.Definitions.Concat(asset.Metadata).ToArray();
        return new AssetText(asset.Id, ReadField(sources, NameFields, issues),
            ReadField(sources, DescriptionFields, issues));
    }

    public static IReadOnlyList<LocalizedText> Localize(IFileProvider provider, IReadOnlyList<AssetText> assets,
        ICollection<ExtractionIssue> issues)
    {
        var rows = new List<LocalizedText>();
        var cultures = provider.Internationalization.AvailableCultures
            .Distinct(StringComparer.OrdinalIgnoreCase).Order(StringComparer.OrdinalIgnoreCase).ToArray();
        if (cultures.Length == 0)
            issues.Add(new ExtractionIssue("localization", "cultures", "No available cultures were reported by the game."));
        foreach (var culture in cultures)
        {
            var locale = culture.Replace('-', '_').ToLowerInvariant();
            try
            {
                provider.ChangeCulture(culture);
                if (!string.Equals(provider.Internationalization.Culture, culture, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException($"Culture {culture} resolved to {provider.Internationalization.Culture}.");
                if (provider.Internationalization.Count == 0)
                    throw new InvalidDataException($"No localization entries loaded for {culture}.");
            }
            catch (Exception error)
            {
                issues.Add(new ExtractionIssue("localization", locale, error.Message));
                continue;
            }

            foreach (var asset in assets)
            {
                var name = Resolve(asset.Name, provider.Internationalization, locale);
                var description = Resolve(asset.Description, provider.Internationalization, locale);
                if (name.Length > 0 || description.Length > 0)
                    rows.Add(new LocalizedText(asset.AssetId, locale, name, description));
            }
        }
        return rows;
    }

    internal static string Resolve(TextReference? text,
        IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> translations, string locale)
    {
        if (text is null) return string.Empty;
        if (text.CultureInvariant) return text.Source;
        if (text.Key.Length > 0 && translations.TryGetValue(text.Namespace, out var entries)
            && entries.TryGetValue(text.Key, out var value))
            return value;
        return locale == "en" ? text.Source : string.Empty;
    }

    private static TextReference? ReadField(IReadOnlyList<UObject> sources, string[] fields,
        ICollection<ExtractionIssue> issues)
    {
        foreach (var field in fields)
        {
            TextReference? selected = null;
            string? selectedPath = null;
            foreach (var source in sources)
            {
                var path = $"{source.GetPathName()}.{field}";
                try
                {
                    if (!Properties.TryGet<FText>(source, field, out var text)) continue;
                    var reference = ReadReference(text, source.Owner?.Provider, path, issues);
                    if (reference is null || (reference.Key.Length == 0 && reference.Source.Length == 0))
                        continue;
                    if (selected is null)
                    {
                        selected = reference;
                        selectedPath = path;
                    }
                    else if (reference != selected)
                    {
                        issues.Add(new ExtractionIssue("text", path,
                            $"Conflicting {field} text references at {selectedPath} and {path}; retaining the first reference."));
                    }
                }
                catch (Exception error)
                {
                    issues.Add(new ExtractionIssue("text", path, error.Message));
                }
            }
            if (selected is not null) return selected;
        }
        return null;
    }

    private static TextReference? ReadReference(FText text, IFileProvider? provider, string path,
        ICollection<ExtractionIssue> issues)
    {
        var invariant = text.Flags.HasFlag(ETextFlag.CultureInvariant);
        switch (text.TextHistory)
        {
            case FTextHistory.Base value:
                return new TextReference(value.Namespace, value.Key, value.SourceString, invariant);
            case FTextHistory.None value:
                return new TextReference(string.Empty, string.Empty, value.CultureInvariantString ?? string.Empty, CultureInvariant: true);
            case FTextHistory.StringTableEntry value:
                if (provider is not null && provider.TryLoadPackageObject<UStringTable>(value.TableId.Text, out var table)
                    && table.StringTable.KeysToEntries.TryGetValue(value.Key, out var source))
                    return new TextReference(table.StringTable.TableNamespace, value.Key, source, invariant);
                issues.Add(new ExtractionIssue("text", path,
                    $"String table entry could not be loaded: {value.TableId.Text}:{value.Key}."));
                return null;
            default:
                issues.Add(new ExtractionIssue("text", path, $"Unsupported text history: {text.HistoryType}."));
                return null;
        }
    }
}
