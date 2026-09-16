using System.Text.Json;
using AssetIndex;
using static PublishSnapshot.Preview;
using static PublishSnapshot.ResourceEvidence;

namespace PublishSnapshot;

internal static class TextRoleChecks
{
    public static IEnumerable<string> RootFields => TextRolePolicy.PropertyNames.Append("ContainerName");

    private sealed record CandidateKey(string Kind, string Source, string Field)
    {
        public static CandidateKey Create(string kind, string source, string field) =>
            new(kind, source.ToUpperInvariant(), field.ToUpperInvariant());
    }
    private sealed record ExpectedText(string Kind, string Source, string Class, TextField Field, string Owner);

    public static void Validate(JsonElement assets, IdentityContext identities)
    {
        foreach (var asset in assets.EnumerateArray())
        {
            var expected = Expected(asset, identities);
            var observed = new HashSet<CandidateKey>();
            foreach (var candidate in asset.GetProperty("presentation").GetProperty("candidates").EnumerateArray())
            {
                var source = ObjectPath(candidate, "sourcePath");
                var kind = String(candidate, "sourceKind");
                var field = String(candidate, "field");
                var key = CandidateKey.Create(kind, source, field);
                Require(expected.TryGetValue(key, out var text),
                    $"Unsupported text candidate {kind}: {source}.{field}.");
                Require(observed.Add(key), $"Duplicate text candidate {kind}: {source}.{field}.");
                Require(text!.Class.Equals(String(candidate, "sourceClass"), StringComparison.OrdinalIgnoreCase),
                    "Text candidate class differs from its decoded source.");
                Require(text.Field.Role == String(candidate, "role"),
                    $"Text candidate role differs from its class and field: {source}.{field}.");
                Require(text.Owner.Equals(ObjectPath(candidate, "definedAt"), StringComparison.OrdinalIgnoreCase),
                    "Text candidate owner differs from its typed template chain.");
            }
            foreach (var (key, text) in expected)
                Require(observed.Contains(key), $"Missing text candidate {text.Kind}: {text.Source}.{text.Field.Name}.");
        }
    }

    private static Dictionary<CandidateKey, ExpectedText> Expected(JsonElement asset, IdentityContext identities)
    {
        var result = new Dictionary<CandidateKey, ExpectedText>();
        foreach (var (kind, source) in Sources(asset))
        {
            var schema = identities.Schema(source);
            foreach (var field in Roles(schema, kind))
            {
                Require(schema.Property(field.Name, "TextProperty") is not null, "Text role has no native property declaration.");
                // Missing fields emit no candidate. An authored empty FText still
                // owns its tier and must remain present to prevent a false fallback.
                if (identities.Field(source, field.Name, "TextProperty") is not { } origin) continue;
                result.TryAdd(CandidateKey.Create(kind, source, field.Name),
                    new(kind, source, schema.SourceClass, field, origin.Owner.Path));
            }
        }
        return result;
    }

    private static IEnumerable<(string Kind, string Source)> Sources(JsonElement asset)
    {
        foreach (var (family, kind) in new[] { ("definitions", "definition"), ("metadata", "metadata") })
            foreach (var source in asset.GetProperty(family).EnumerateArray())
                yield return (kind, ObjectPath(source, "path"));
        var presentation = asset.GetProperty("presentation");
        foreach (var (family, kind) in new[] { ("containers", "container"), ("visualSlots", "visual-slot"), ("inventoryRoots", "inventory-root") })
            foreach (var relation in presentation.GetProperty(family).EnumerateArray())
                yield return (kind, ObjectPath(relation, "metadataPath"));
    }

    private static IReadOnlyList<TextField> Roles(IdentitySchema schema, string kind)
    {
        if (kind is "container" or "inventory-root")
        {
            Require(schema.IsA("UIInventoryContainerMetaDataItem"), "Container text has the wrong native owner.");
            return [new("ContainerName", "display-name")];
        }
        Require(kind is "definition" or "metadata" or "visual-slot", "Unsupported text source kind.");
        if (kind == "visual-slot")
            Require(schema.IsA("UICharacterCustomizationQuickNavTabMetaDataItem"), "Visual slot text has the wrong native owner.");
        return TextRolePolicy.For(schema.NativeAncestry);
    }
}
