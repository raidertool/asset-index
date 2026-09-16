using System.Text.RegularExpressions;
using CUE4Parse.FileProvider;
using CUE4Parse.FileProvider.Objects;
using CUE4Parse.UE4.Localization;
using CUE4Parse.UE4.VirtualFileSystem;

namespace AssetIndex;

internal static class Localization
{
    private sealed record Entry(string Value, uint SourceHash);
    private sealed record OriginEntry(Entry Entry, string Origin);

    public static IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> Load(
        IFileProvider provider, string culture)
    {
        ValidateCulture(provider, culture);
        var merged = new Dictionary<(string Namespace, string Key), OriginEntry>();
        foreach (var path in Inputs(provider, culture))
        {
            var file = provider.Files[path];
            var entries = ReadEffective(provider, file);
            foreach (var (key, entry) in entries)
            {
                if (merged.TryGetValue(key, out var previous) && previous.Entry != entry)
                    throw new InvalidDataException($"Conflicting localization {culture} {key.Namespace}:{key.Key} in " +
                        $"{previous.Origin} and {Origin(file)} (text or source hash differs).");
                merged.TryAdd(key, new(entry, Origin(file)));
            }
        }
        if (merged.Count == 0)
            throw new InvalidDataException($"No localization entries loaded for {culture}.");
        return merged.GroupBy(pair => pair.Key.Namespace, StringComparer.Ordinal).ToDictionary(
            group => group.Key,
            group => (IReadOnlyDictionary<string, string>)group.ToDictionary(pair => pair.Key.Key,
                pair => pair.Value.Entry.Value, StringComparer.Ordinal), StringComparer.Ordinal);
    }

    internal static IReadOnlyList<string> Inputs(IFileProvider provider, string culture)
    {
        // Keep the upstream project-file scope. Sorting only stabilizes diagnostics; it never picks a conflicting value.
        var pattern = new Regex($"^(?!Engine).+/.+/{Regex.Escape(culture)}/.+\\.locres$", RegexOptions.IgnoreCase);
        return provider.Files.Keys.Where(path => pattern.IsMatch(path)).Distinct(provider.PathComparer)
            .Order(StringComparer.Ordinal).ToArray();
    }

    private static void ValidateCulture(IFileProvider provider, string culture)
    {
        var localization = provider.Internationalization;
        if (!localization.AvailableCultures.Contains(culture, provider.PathComparer))
            throw new InvalidDataException($"'{culture}' is not a valid culture.");
        if (localization.CultureMappings.TryGetValue(culture, out var mapped) &&
            localization.AvailableCultures.Contains(mapped, provider.PathComparer) &&
            !string.Equals(culture, mapped, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException($"Culture {culture} resolved to {mapped}.");
    }

    private static Dictionary<(string Namespace, string Key), Entry> ReadEffective(IFileProvider provider, GameFile selected)
    {
        var entries = ReadFile(selected);
        provider.Files.TryGetValues(selected.Path, out var copies);
        foreach (var copy in copies.Distinct())
        {
            if (ReferenceEquals(copy, selected)) continue;
            if (selected is VfsEntry mounted && copy is VfsEntry other && other.Vfs.ReadOrder < mounted.Vfs.ReadOrder)
                continue;
            // Equal/unknown mount priority is not a proven override. Equivalent copies are harmless.
            var peer = ReadFile(copy);
            if (entries.Count != peer.Count || entries.Any(pair => peer.GetValueOrDefault(pair.Key) != pair.Value))
                throw new InvalidDataException($"Ambiguous localization mount for {Origin(selected)} and {Origin(copy)}.");
        }
        return entries;
    }

    private static Dictionary<(string Namespace, string Key), Entry> ReadFile(GameFile file)
    {
        try
        {
            using var archive = file.CreateReader();
            var resource = new FTextLocalizationResource(archive);
            var entries = new Dictionary<(string, string), Entry>();
            foreach (var (space, values) in resource.Entries)
                foreach (var (key, entry) in values)
                    entries.Add((space.Str, key.Str), new(entry.LocalizedString, entry.SourceStringHash));
            return entries;
        }
        catch (Exception error)
        {
            throw new InvalidDataException($"Could not read localization input {Origin(file)}: {error.Message}", error);
        }
    }

    private static string Origin(GameFile file) => file is VfsEntry entry
        ? $"{file.Path} ({entry.Vfs.Name})" : file.Path;
}
