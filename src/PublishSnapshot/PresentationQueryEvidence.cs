using System.Globalization;
using AssetIndex;
using static PublishSnapshot.Preview;

namespace PublishSnapshot;

// Read only the typed query and tag structures emitted by discovery. Property
// ordinals identify serialized fields; their names identify the gameplay fields.
internal static class PresentationQueryEvidence
{
    public static VisualSlotQuery Query(EvidenceField field)
    {
        Type(field, "StructProperty");
        if (field.Child("TokenStreamVersion") is { } version)
        {
            Type(version, "IntProperty");
            var value = version.Value("integer");
            Require(value.Type == "IntProperty" && value.Value == "0", "Unsupported gameplay-tag query version.");
        }
        var dictionary = Child(field, "TagDictionary", "ArrayProperty");
        var tags = Elements(dictionary).Select(index => DictionaryTag(dictionary, index)).ToArray();
        var tokens = Child(field, "QueryTokenStream", "ArrayProperty").Value("binary-base64");
        Require(tokens.Type == "ByteProperty[]", "Gameplay-tag query token stream is not bytes.");
        byte[] bytes;
        try { bytes = Convert.FromBase64String(tokens.Value ?? throw new InvalidDataException("Missing query token stream.")); }
        catch (FormatException error) { throw new InvalidDataException("Invalid query token bytes.", error); }
        Require(Convert.ToBase64String(bytes) == tokens.Value, "Gameplay-tag query bytes use noncanonical base64.");
        var query = new VisualSlotQuery(field.Owner.Path, tags, bytes);
        // An empty tag set still validates every branch of the complete stream.
        GameplayTagQueryMatch.Matches(query, []);
        return query;
    }

    private static string DictionaryTag(EvidenceField dictionary, int index)
    {
        var prefix = dictionary.Header.Pointer + "/" + index;
        Require(dictionary.Owner.Values.Count(value => value.Pointer.StartsWith(prefix + "/", StringComparison.Ordinal)) == 1 &&
            !dictionary.Owner.References.Any(value => value.Pointer.StartsWith(prefix + "/", StringComparison.Ordinal)) &&
            !dictionary.Owner.TextPointers.Any(pointer => pointer.StartsWith(prefix + "/", StringComparison.Ordinal)),
            "Gameplay-tag dictionary entry has ambiguous values.");
        if (dictionary.Owner.Field("TagName", prefix + "/Properties") is { } entry) return Name(entry);
        var native = dictionary.Owner.Values.Where(value => value.Pointer == prefix + "/TagName").ToArray();
        Require(native.Length == 1 && native[0].Type == "FName" && native[0].Kind == "name",
            "Gameplay-tag dictionary entry has no typed TagName.");
        return GameplayTagQueryMatch.Tag(native[0].Value ?? "");
    }

    public static string[] Tags(EvidenceField? field)
    {
        if (field is null) return [];
        Type(field, "StructProperty");
        var prefix = field.Header.Pointer + "/GameplayTags/";
        var subtree = field.Header.Pointer + "/";
        var values = field.Owner.Values.Where(value => value.Pointer == field.Header.Pointer || value.Pointer.StartsWith(subtree, StringComparison.Ordinal)).ToArray();
        Require(!field.Owner.References.Any(value => value.Pointer == field.Header.Pointer || value.Pointer.StartsWith(subtree, StringComparison.Ordinal)) &&
            !field.Owner.TextPointers.Any(pointer => pointer == field.Header.Pointer || pointer.StartsWith(subtree, StringComparison.Ordinal)),
            "Gameplay-tag container has conflicting reference or text evidence.");
        if (values.Length == 1 && values[0].Pointer == field.Header.Pointer + "/GameplayTags" &&
            values[0].Type == "FGameplayTag[]" && values[0].Kind == "empty-array" && values[0].Value is null) return [];
        Require(values.Length > 0, "Gameplay-tag container has no decoded array.");
        var tags = new SortedDictionary<int, string>();
        foreach (var value in values)
        {
            Require(value.Pointer.StartsWith(prefix, StringComparison.Ordinal), "Gameplay-tag container has an unsupported value.");
            var tail = value.Pointer[prefix.Length..];
            var split = tail.IndexOf('/');
            Require(split > 0 && tail[split..] == "/TagName" && value.Type == "FName" && value.Kind == "name",
                "Gameplay-tag container has an unsupported value.");
            var index = Index(tail[..split]);
            Require(tags.TryAdd(index, GameplayTagQueryMatch.Tag(value.Value ?? "")), "Duplicate gameplay-tag index.");
        }
        for (var index = 0; index < tags.Count; index++)
            Require(tags.ContainsKey(index), "Gameplay-tag indices must be contiguous.");
        return tags.Values.ToArray();
    }

    public static string Tag(EvidenceField field) => Name(Child(field, "TagName", "NameProperty"));

    public static EvidenceField Child(EvidenceField field, string name, params string[] kinds)
    {
        var child = field.Child(name) ?? throw new InvalidDataException($"Missing presentation field {field.Owner.Path}.{name}.");
        Type(child, kinds);
        return child;
    }

    public static void Type(EvidenceField field, params string[] kinds)
    {
        Require(field.Header.SerializeType == "Property", "Presentation property has no decoded tagged value.");
        Require(kinds.Contains(field.Header.Type, StringComparer.OrdinalIgnoreCase), "Presentation property has the wrong type.");
        Require(field.Header.ArrayIndex is null or 0 && field.Header.ArraySize is null or 1,
            "Presentation property is not scalar.");
    }

    public static string Name(EvidenceField field)
    {
        Type(field, "NameProperty");
        var value = field.Value("name");
        Require(value.Type == "NameProperty", "Gameplay-tag name has the wrong scalar type.");
        return GameplayTagQueryMatch.Tag(value.Value ?? "");
    }

    public static int[] Elements(EvidenceField field)
    {
        var prefix = field.Header.Pointer + "/";
        var pointers = field.Owner.Properties.Select(value => value.Pointer)
            .Concat(field.Owner.Values.Select(value => value.Pointer))
            .Concat(field.Owner.References.Select(value => value.Pointer)).Concat(field.Owner.TextPointers);
        var indices = pointers.Where(pointer => pointer.StartsWith(prefix, StringComparison.Ordinal))
            .Select(pointer => pointer[prefix.Length..].Split('/')[0]).Select(Index).Distinct().Order().ToArray();
        var values = field.Owner.Values.Where(value => value.Pointer == field.Header.Pointer).ToArray();
        Require(!field.Owner.References.Any(value => value.Pointer == field.Header.Pointer) && !field.Owner.TextPointers.Contains(field.Header.Pointer),
            "Presentation array has conflicting reference or text evidence.");
        Require(indices.Length > 0 ? values.Length == 0 : values.Length == 1 && values[0].Kind == "empty-array" && values[0].Value is null,
            "Presentation array has missing or contradictory element evidence.");
        for (var index = 0; index < indices.Length; index++)
            Require(indices[index] == index, "Presentation array indices must be contiguous.");
        return indices;
    }

    private static int Index(string text)
    {
        Require(int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out var index) && index >= 0 &&
            text == index.ToString(CultureInfo.InvariantCulture), "Invalid presentation array index.");
        return index;
    }
}
