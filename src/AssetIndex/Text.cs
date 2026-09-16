using CUE4Parse.FileProvider;
using CUE4Parse.MappingsProvider;
using CUE4Parse.UE4.Assets.Exports;
using CUE4Parse.UE4.Assets.Exports.Internationalization;
using CUE4Parse.UE4.Objects.Core.i18N;

namespace AssetIndex;

internal sealed record TextReference(string Namespace, string Key, string Source, bool CultureInvariant = false);
internal sealed record TextCandidate(string Role, string SourceKind, string SourcePath, string SourceClass,
    string Field, string DefinedAt, TextReference Reference);
internal sealed record AssetText(long AssetId, TextReference? Name, TextReference? Description)
{
    public IReadOnlyList<TextCandidate> Candidates { get; init; } = [];
    public IReadOnlyList<ExtractionIssue> Notices { get; init; } = [];
}
internal sealed record LocalizedText(long AssetId, string Locale, string DisplayName, string Description);

internal static class Text
{
    public static AssetText Read(CatalogAsset asset, ICollection<ExtractionIssue> issues)
    {
        var candidates = asset.Metadata.SelectMany(source => source.Text.Select(candidate => candidate with { SourceKind = "metadata" }))
            .Concat(asset.Definitions.SelectMany(source => source.Text.Select(candidate => candidate with { SourceKind = "definition" })))
            .Concat(asset.PresentationNames.SelectMany(name => name.Text))
            .Concat(asset.VisualSlotNames.SelectMany(name => name.Text)).ToArray();
        foreach (var issue in asset.PresentationNames.SelectMany(name => name.TextIssues).Distinct()) issues.Add(issue);
        foreach (var issue in asset.VisualSlotNames.SelectMany(name => name.TextIssues).Distinct()) issues.Add(issue);

        var distinct = candidates.Distinct().OrderBy(candidate => candidate.SourcePath, StringComparer.Ordinal)
            .ThenBy(candidate => candidate.Field, StringComparer.Ordinal).ToArray();
        var notices = new List<ExtractionIssue>();
        var description = Select(distinct, ["description", "tooltip"], notices);
        return new AssetText(asset.Id, SelectName(asset, distinct, description, notices), description)
        { Candidates = distinct, Notices = notices };
    }

    private static TextReference? SelectName(CatalogAsset asset, IReadOnlyList<TextCandidate> candidates,
        TextReference? description, ICollection<ExtractionIssue> notices)
    {
        if (asset.Definitions.Any(source => source.Reference.Class == "NPCItemDataAsset"))
        {
            // NPC presentation owns the visible NPC label; generic item metadata remains evidence.
            var npcNames = candidates.Where(candidate => candidate.SourceKind == "metadata" &&
                candidate.SourceClass == "UINPCMetaDataItem" && candidate.Field == "DisplayName" &&
                candidate.Role == "display-name").ToArray();
            if (npcNames.Length > 0) return Select(npcNames, ["display-name"], notices);
        }

        string[] roles = ["display-name", "title", "short-name"];
        if (candidates.Any(candidate => roles.Contains(candidate.Role)))
            return Select(candidates, roles, notices);

        // Modifiers use their Description as a presentation label. Keep its original role/field.
        if (asset.Definitions.Any(source => source.Reference.Class == "SessionModifierDataAsset") &&
            candidates.Any(candidate => candidate.Reference == description && IsModifierDescription(candidate)))
            return description;
        return null;
    }

    private static bool IsModifierDescription(TextCandidate candidate) =>
        candidate.Role == "description" && candidate.Field == "Description" &&
        (candidate.SourceKind, candidate.SourceClass) is ("definition", "SessionModifierDataAsset") or
            ("metadata", "UISessionModifierMetaDataItem");

    public static IReadOnlyList<TextCandidate> Capture(UObject source, TypeMappings? mappings,
        ICollection<ExtractionIssue> issues)
    {
        var candidates = new List<TextCandidate>();
        try
        {
            foreach (var field in TextRoles.For(source, mappings ?? source.Owner?.Mappings))
                ReadCandidate(source, field, "", candidates, issues);
        }
        catch (Exception error) { issues.Add(new("text", ObjectMetadata.Path(source), error.Message)); }
        return candidates;
    }

    public static IReadOnlyList<TextCandidate> CaptureContainer(UObject source, ICollection<ExtractionIssue> issues)
    {
        var candidates = new List<TextCandidate>();
        ReadCandidate(source, new("ContainerName", "display-name"), "container", candidates, issues);
        return candidates;
    }

    public static IReadOnlyList<LocalizedText> Localize(IFileProvider provider, IReadOnlyList<AssetText> assets,
        ICollection<ExtractionIssue> issues,
        Action<string, IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>>>? observe = null)
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

            observe?.Invoke(locale, provider.Internationalization);
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

    private static void ReadCandidate(UObject source, TextField field, string kind,
        ICollection<TextCandidate> candidates, ICollection<ExtractionIssue> issues)
    {
        var path = $"{ObjectMetadata.Path(source)}.{field.Name}";
        try
        {
            if (Properties.Find(source, field.Name, out var definedAt) is not { } property) return;
            if (property.Tag?.GetValue(typeof(FText)) is not FText text)
                throw new InvalidDataException($"Cannot read {path} as FText.");
            var reference = ReadReference(text, source.Owner?.Provider, path, issues);
            if (reference is not null)
                candidates.Add(new(field.Role, kind, ObjectMetadata.Path(source), source.ExportType, property.Name.Text,
                    ObjectMetadata.Path(definedAt!), reference));
        }
        catch (Exception error) { issues.Add(new("text", path, error.Message)); }
    }

    private static TextReference? Select(IReadOnlyList<TextCandidate> candidates, string[] roles,
        ICollection<ExtractionIssue> notices)
    {
        // UI presentation owns its labels. Definition text and contextual container labels
        // remain candidates with provenance even when the UI supplies the primary value.
        foreach (var kind in new[] { "metadata", "definition", "container", "visual-slot" })
            foreach (var role in roles)
            {
                var peers = candidates.Where(candidate => candidate.SourceKind == kind && candidate.Role == role).ToArray();
                if (peers.Length == 0) continue;
                var references = peers.Select(candidate => candidate.Reference).Distinct().ToArray();
                if (references.Length == 1)
                    return references[0].Key.Length == 0 && references[0].Source.Length == 0 ? null : references[0];
                var paths = string.Join(", ", peers.Select(candidate => $"{candidate.SourcePath}.{candidate.Field}"));
                notices.Add(new("text", paths, $"Conflicting {role} references; no primary value selected. Candidates: {paths}."));
                return null;
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
