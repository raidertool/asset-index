namespace AssetIndex;

// Source defaults remain separate from translations missing in a locale.
internal static class CatalogText
{
    public static string Resolve(TextReference? text,
        IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> translations)
    {
        if (text is null) return string.Empty;
        if (text.CultureInvariant) return text.Source;
        if (text.Key.Length > 0 && translations.TryGetValue(text.Namespace, out var entries)
            && entries.TryGetValue(text.Key, out var value))
            return value;
        return string.Empty;
    }
}
