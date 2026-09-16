using System.Text.Json;
using AssetIndex;
using static PublishSnapshot.Preview;
using static PublishSnapshot.ResourceEvidence;

namespace PublishSnapshot;

internal static class TextRoleChecks
{
    public static IEnumerable<string> RootFields => TextRolePolicy.PropertyNames.Append("ContainerName");

    public static void Validate(JsonElement assets, IdentityContext identities)
    {
        foreach (var asset in assets.EnumerateArray())
            foreach (var candidate in asset.GetProperty("presentation").GetProperty("candidates").EnumerateArray())
            {
                var source = ObjectPath(candidate, "sourcePath");
                var schema = identities.Schema(source);
                Require(schema.SourceClass.Equals(String(candidate, "sourceClass"), StringComparison.OrdinalIgnoreCase),
                    "Text candidate class differs from its decoded source.");
                var fields = Roles(schema, String(candidate, "sourceKind"));
                foreach (var declared in fields)
                    Require(schema.Property(declared.Name, "TextProperty") is not null, "Text role has no native property declaration.");
                var field = String(candidate, "field");
                var policy = fields.SingleOrDefault(value => value.Name.Equals(field, StringComparison.OrdinalIgnoreCase));
                Require(policy is not null && policy.Role == String(candidate, "role"),
                    $"Text candidate role differs from its class and field: {source}.{field}.");
                var origin = identities.Field(source, field, "TextProperty");
                Require(origin is not null && origin.Owner.Path.Equals(ObjectPath(candidate, "definedAt"), StringComparison.OrdinalIgnoreCase),
                    "Text candidate owner differs from its typed template chain.");
            }
    }

    private static IReadOnlyList<TextField> Roles(IdentitySchema schema, string kind)
    {
        if (kind == "container")
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
