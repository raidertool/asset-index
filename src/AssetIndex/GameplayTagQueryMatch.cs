using CUE4Parse.UE4.Assets.Objects;
using CUE4Parse.UE4.Assets.Objects.Properties;
using CUE4Parse.UE4.Objects.UObject;
using CUE4Parse.UE4.Objects.GameplayTags;

namespace AssetIndex;

internal sealed record VisualSlotQuery(string DefinedAt, IReadOnlyList<string> TagDictionary, IReadOnlyList<byte> Tokens);

// Validate the whole stream, including skipped branches. Unreal's non-exact queries
// test HasTag, which includes parents; CUE4Parse's container only stores explicit tags.
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
        Validate(query);
        var present = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var value in tags)
        {
            var tag = Tag(value);
            present.Add(tag);
            for (var end = tag.LastIndexOf('.'); end > 0; end = tag.LastIndexOf('.', end - 1))
                present.Add(tag[..end]);
        }
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
        if (string.IsNullOrWhiteSpace(value) || value != value.Trim(' ') || value.IndexOfAny(['\r', '\n', '\t']) >= 0 ||
            value.Equals("None", StringComparison.OrdinalIgnoreCase) ||
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

    private static void Validate(VisualSlotQuery query)
    {
        foreach (var tag in query.TagDictionary) Tag(tag);
        var cursor = 0;
        if (Token() != 0) throw new InvalidDataException("Unsupported gameplay-tag query version.");
        var root = Token();
        if (root > 1) throw new InvalidDataException("Invalid gameplay-tag root marker.");
        if (root == 1) Expression(0);
        if (cursor != query.Tokens.Count) throw new InvalidDataException("Gameplay-tag query has trailing tokens.");

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
                }
            }
        }
    }
}
