using CUE4Parse.UE4.Assets.Objects;
using CUE4Parse.UE4.Assets.Objects.Properties;
using CUE4Parse.UE4.Objects.UObject;
using CUE4Parse.UE4.Objects.GameplayTags;

namespace AssetIndex;

// Validate the whole stream, including skipped branches. Unreal's non-exact queries
// test HasTag, which includes parents; CUE4Parse's container only stores explicit tags.
internal static partial class GameplayTagQueryMatch
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
}
