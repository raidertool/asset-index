using CUE4Parse.UE4.Assets.Objects;
using CUE4Parse.UE4.Assets.Objects.Properties;
using CUE4Parse.UE4.Objects.UObject;
using CUE4Parse.UE4.Objects.GameplayTags;

namespace AssetIndex;

internal sealed record VisualSlotQuery(string DefinedAt, IReadOnlyList<string> TagDictionary, IReadOnlyList<byte> Tokens);

// Upstream evaluates the six expression kinds, but does not validate skipped branches,
// stream versions, or parent tags. Reject those uncertainties before using its result.
internal static class GameplayTagQueryMatch
{
    public static VisualSlotQuery Capture(FStructFallback source, string definedAt)
    {
        var dictionary = Properties.Get<UScriptArray>(source, "TagDictionary", definedAt).Properties.Select(ReadTag).ToArray();
        var tokens = Properties.Get<UScriptArray>(source, "QueryTokenStream", definedAt).Properties.Select(value =>
            value.GenericValue is byte token ? token : throw new InvalidDataException("Gameplay-tag token is not a byte.")).ToArray();
        var version = Properties.Find(source, "TokenStreamVersion", definedAt);
        if (version is not null && version.Tag?.GetValue(typeof(int)) is not 0)
            throw new InvalidDataException("Unsupported gameplay-tag query version.");
        var query = new VisualSlotQuery(definedAt, dictionary, tokens);
        Validate(query);
        return query;
    }

    public static bool Matches(VisualSlotQuery query, IReadOnlyList<string> tags)
    {
        var referenced = Validate(query);
        var present = new HashSet<string>(tags.Select(Tag), StringComparer.OrdinalIgnoreCase);
        foreach (var tag in referenced.Select(index => query.TagDictionary[index]))
            if (!present.Contains(tag) && present.Any(candidate => candidate.StartsWith(tag + ".", StringComparison.OrdinalIgnoreCase)))
                throw new InvalidDataException("Gameplay-tag parent matching requires a verified evaluator.");
        var evaluator = new FGameplayTagQuery(new FStructFallback())
        {
            TokenStreamVersion = EGameplayTagQueryStreamVersion.InitialVersion,
            TagDictionary = query.TagDictionary.Select(tag => new FGameplayTag(tag)).ToArray(),
            QueryTokenStream = query.Tokens.ToArray()
        };
        return evaluator.Matches(new FGameplayTagContainer(present.Select(tag => new FGameplayTag(tag)).ToArray()));
    }

    internal static string Tag(string value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Equals("None", StringComparison.OrdinalIgnoreCase) ||
            value.Split('.').Any(part => part.Length == 0))
            throw new InvalidDataException("Gameplay tag is empty or malformed.");
        return value;
    }

    private static string ReadTag(FPropertyTagType property)
    {
        var tag = property.GenericValue is FScriptStruct value ? value.StructType switch
        {
            FStructFallback fallback => Properties.Get<FName>(fallback, "TagName", "TagDictionary").Text,
            FGameplayTag native => native.TagName.Text,
            _ => throw new InvalidDataException("Gameplay-tag dictionary entry has the wrong struct type.")
        } : throw new InvalidDataException("Gameplay-tag dictionary entry is not a struct.");
        return Tag(tag);
    }

    private static HashSet<byte> Validate(VisualSlotQuery query)
    {
        var referenced = new HashSet<byte>();
        var cursor = 0;
        if (Token() != 0) throw new InvalidDataException("Unsupported gameplay-tag query version.");
        var root = Token();
        if (root > 1) throw new InvalidDataException("Invalid gameplay-tag root marker.");
        if (root == 1) Expression(0);
        if (cursor != query.Tokens.Count) throw new InvalidDataException("Gameplay-tag query has trailing tokens.");
        return referenced;

        byte Token() => cursor < query.Tokens.Count ? query.Tokens[cursor++]
            : throw new InvalidDataException("Gameplay-tag query is truncated.");

        void Expression(int depth)
        {
            if (depth > 64) throw new InvalidDataException("Gameplay-tag query exceeds 64 nested expressions.");
            var kind = Token();
            if (kind is < 1 or > 6) throw new InvalidDataException("Unsupported gameplay-tag expression.");
            var count = Token();
            for (var i = 0; i < count; i++)
            {
                if (kind > 3) Expression(depth + 1);
                else
                {
                    var index = Token();
                    if (index >= query.TagDictionary.Count) throw new InvalidDataException("Gameplay-tag dictionary index is out of range.");
                    referenced.Add(index);
                }
            }
        }
    }
}
