using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using AssetIndex;
using static PublishSnapshot.Preview;
using static PublishSnapshot.ResourceEvidence;

namespace PublishSnapshot;

internal static class ImageOriginChecks
{
    public static IEnumerable<string> RootFields => ImageFields.Simple.Concat(ImageFields.Typed.Keys)
        .Concat(ImageFields.Nested.Select(item => item.Root));

    public static void ValidateResources(JsonElement resources, IdentityContext identities)
    {
        foreach (var resource in resources.EnumerateArray())
        {
            var schema = identities.Schema(ObjectPath(resource, "path"));
            Require(schema.IsA("Texture2D") || schema.IsA("MaterialInstanceConstant") || schema.IsA("Material"),
                "Exported image resource has an unsupported native class.");
        }
    }

    public static void Validate(JsonElement assets, IdentityContext identities)
    {
        foreach (var asset in assets.EnumerateArray())
        {
            var declared = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
            foreach (var image in asset.GetProperty("images").EnumerateArray())
            {
                var source = ObjectPath(image, "source");
                var field = String(image, "field");
                if (!declared.TryGetValue(source, out var fields))
                    declared.Add(source, fields = new(StringComparer.OrdinalIgnoreCase));
                Require(fields.Add(field), $"Duplicate image association: {source}.{field}.");
                var property = Resolve(source, field, identities);
                var target = property.Reference();
                var expected = image.GetProperty("resource").ValueKind == JsonValueKind.Null ? null : ObjectPath(image, "resource");
                Require(string.Equals(target, expected, StringComparison.OrdinalIgnoreCase),
                    $"Image resource differs from the decoded field: {source}.{field}.");
                Require(String(image, "status") == (target is null ? "absent" : "exported"),
                    $"Image status differs from the decoded field: {source}.{field}.");
            }
            foreach (var source in asset.GetProperty("definitions").EnumerateArray()
                .Concat(asset.GetProperty("metadata").EnumerateArray()))
            {
                var path = ObjectPath(source, "path");
                foreach (var field in ExpectedFields(path, identities))
                    Require(declared.TryGetValue(path, out var fields) && fields.Remove(field),
                        $"Catalog omits decoded image association: {path}.{field}.");
            }
            Require(declared.Values.All(fields => fields.Count == 0), "Image association has no matching catalog source.");
        }
    }

    private static IEnumerable<string> ExpectedFields(string source, IdentityContext identities)
    {
        foreach (var field in ImageFields.Simple)
            if (identities.FindField(source, field) is { } found) yield return found.Header.Name;
        var schema = identities.Schema(source);
        foreach (var (field, owner) in ImageFields.Typed)
            if (schema.IsA(owner) && identities.FindField(source, field) is { } found) yield return found.Header.Name;
        foreach (var nested in ImageFields.Nested)
        {
            if (!schema.IsA(nested.Owner) || identities.FindField(source, nested.Root) is null) continue;
            var root = NestedRoot(source, nested, identities);
            var suffixes = nested.IsArray
                ? PresentationQueryEvidence.Elements(root).Select(index => "/" + index.ToString(CultureInfo.InvariantCulture))
                : [""];
            foreach (var suffix in suffixes)
                if (root.Owner.Field(nested.Leaf, root.Header.Pointer + suffix + "/Properties") is { } leaf)
                    yield return root.Header.Name + (nested.IsArray ? "[" + suffix[1..] + "]" : "") + "." + leaf.Header.Name;
        }
    }

    private static EvidenceField Resolve(string source, string field, IdentityContext identities)
    {
        if (ImageFields.Simple.Contains(field, StringComparer.OrdinalIgnoreCase))
            return Required(identities.FindField(source, field), source, field);
        var typed = ImageFields.Typed.FirstOrDefault(pair => pair.Key.Equals(field, StringComparison.OrdinalIgnoreCase));
        if (typed.Key is not null)
        {
            var schema = identities.Schema(source);
            Require(schema.IsA(typed.Value) && schema.Property(field, "SoftObjectProperty") is not null,
                $"Image field has no supported owner or declaration: {source}.{field}.");
            return Required(identities.Field(source, field, "SoftObjectProperty"), source, field);
        }
        foreach (var nested in ImageFields.Nested)
        {
            var pattern = "\\A" + Regex.Escape(nested.Root) + (nested.IsArray ? @"\[(0|[1-9][0-9]*)\]" : "") +
                "\\." + Regex.Escape(nested.Leaf) + "\\z";
            var match = Regex.Match(field, pattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
            if (!match.Success) continue;
            var root = NestedRoot(source, nested, identities);
            var suffix = nested.IsArray ? "/" + ReadIndex(match.Groups[1].Value) : "";
            var leaf = Required(root.Owner.Field(nested.Leaf, root.Header.Pointer + suffix + "/Properties"), source, field);
            Require(leaf.Header.Type == "SoftObjectProperty", "Nested image is not a soft object reference.");
            return leaf;
        }
        throw new InvalidDataException($"Unsupported image field: {source}.{field}.");
    }

    private static EvidenceField NestedRoot(string source, NestedImageField nested, IdentityContext identities)
    {
        var schema = identities.Schema(source);
        var kind = nested.IsArray ? "ArrayProperty" : "StructProperty";
        var declaration = schema.Property(nested.Root, kind)?.Mapping;
        Require(schema.IsA(nested.Owner) && declaration is not null,
            "Nested image field has no supported owner or declaration.");
        var structure = nested.IsArray ? declaration!.InnerType : declaration;
        Require(structure?.Type == "StructProperty" &&
            string.Equals(structure.StructType, nested.Structure, StringComparison.OrdinalIgnoreCase),
            "Nested image field has the wrong declared structure.");
        Require(identities.StructProperty(nested.Structure, nested.Leaf, "SoftObjectProperty") is not null,
            "Nested image leaf has no supported declaration.");
        return Required(identities.Field(source, nested.Root, kind), source, nested.Root);
    }

    private static string ReadIndex(string value)
    {
        Require(int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var index), "Image array index exceeds its range.");
        return index.ToString(CultureInfo.InvariantCulture);
    }

    private static EvidenceField Required(EvidenceField? field, string source, string name) => field
        ?? throw new InvalidDataException($"Image field is missing from decoded evidence: {source}.{name}.");
}
