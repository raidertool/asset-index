using System.Text.Json;
using static PublishSnapshot.Preview;

namespace PublishSnapshot;

internal sealed record LocalizationEvidence(string Locale,
    IReadOnlyDictionary<(string Namespace, string Key), string> Entries);

internal static class LocalizedTextChecks
{
    public static (bool Name, bool Description) Validate(JsonElement text,
        IReadOnlyList<LocalizationEvidence> dictionaries, JsonElement presentation)
    {
        var translations = new Dictionary<string, (string Name, string Description)>(StringComparer.Ordinal);
        foreach (var row in text.EnumerateArray())
        {
            Fields(row, "locale", "displayName", "description");
            Require(translations.TryAdd(String(row, "locale"),
                (String(row, "displayName", allowEmpty: true), String(row, "description", allowEmpty: true))),
                "Duplicate translation locale.");
        }

        var english = (Name: false, Description: false);
        foreach (var dictionary in dictionaries)
        {
            var name = Resolve(presentation.GetProperty("name"), dictionary);
            var description = Resolve(presentation.GetProperty("description"), dictionary);
            var present = translations.Remove(dictionary.Locale, out var actual);
            Require(present, $"Unexpected or missing translation row for {dictionary.Locale}.");
            Require(actual == (name, description), $"Rendered text differs from selected evidence for {dictionary.Locale}.");
            if (dictionary.Locale == "en") english = (name.Length > 0, description.Length > 0);
        }
        Require(translations.Count == 0, "Translation has no localization dictionary.");
        return english;
    }

    private static string Resolve(JsonElement reference, LocalizationEvidence dictionary)
    {
        if (reference.ValueKind == JsonValueKind.Null) return "";
        var source = String(reference, "source", allowEmpty: true);
        if (reference.GetProperty("cultureInvariant").GetBoolean()) return source;
        var key = String(reference, "key", allowEmpty: true);
        if (key.Length > 0 && dictionary.Entries.TryGetValue(
            (String(reference, "namespace", allowEmpty: true), key), out var translated)) return translated;
        return "";
    }
}
