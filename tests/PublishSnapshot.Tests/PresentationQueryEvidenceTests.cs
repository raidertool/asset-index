using AssetIndex;

namespace PublishSnapshot.Tests;

public sealed class PresentationQueryEvidenceTests
{
    [Theory]
    [InlineData(new byte[] { 0, 1, 1, 2, 0, 1 }, true)] // any tags
    [InlineData(new byte[] { 0, 1, 2, 2, 0, 1 }, false)] // all tags
    [InlineData(new byte[] { 0, 1, 3, 2, 0, 1 }, false)] // no tags
    [InlineData(new byte[] { 0, 1, 4, 2, 1, 1, 0, 1, 1, 1 }, true)] // any expressions
    [InlineData(new byte[] { 0, 1, 5, 2, 1, 1, 0, 1, 1, 1 }, false)] // all expressions
    [InlineData(new byte[] { 0, 1, 6, 2, 1, 1, 0, 1, 1, 1 }, false)] // no expressions
    public void EveryExpressionUsesSharedExtractionSemantics(byte[] tokens, bool expected)
    {
        var query = PresentationQueryEvidence.Query(QueryField(tokens));

        Assert.Equal(expected, GameplayTagQueryMatch.Matches(query, ["Item.A.Child"]));
    }

    [Theory]
    [InlineData("Item.A", "Item.A.Child", true)]
    [InlineData("Item.A.Child", "Item.A", false)]
    [InlineData("Item.A", "Item.ABC", false)]
    [InlineData("item.a", "ITEM.A.CHILD", true)]
    public void ParentTagsMatchOnlyInTheGameplayDirection(string required, string present, bool expected)
    {
        var query = new VisualSlotQuery("/Game/Query.Query", [required], [0, 1, 1, 1, 0]);

        Assert.Equal(expected, GameplayTagQueryMatch.Matches(query, [present]));
    }

    [Theory]
    [InlineData(new byte[] { 1, 0 })]
    [InlineData(new byte[] { 0, 2 })]
    [InlineData(new byte[] { 0, 1, 1, 1, 2 })]
    [InlineData(new byte[] { 0, 1, 1, 2, 0 })]
    [InlineData(new byte[] { 0, 0, 0 })]
    [InlineData(new byte[] { 0, 1, 4, 2, 1, 1, 0, 99, 0 })]
    public void InvalidOrSkippedBranchesCannotPass(byte[] tokens)
    {
        Assert.Throws<InvalidDataException>(() => PresentationQueryEvidence.Query(QueryField(tokens)));
    }

    [Theory]
    [InlineData(" Item.A")]
    [InlineData("Item..A")]
    [InlineData("None")]
    [InlineData("Item.\tA")]
    public void MalformedTagNamesCannotProveMembership(string tag)
    {
        Assert.Throws<InvalidDataException>(() => PresentationQueryEvidence.Tags(TagField(tag)));
    }

    [Theory]
    [InlineData("wrong-type")]
    [InlineData("wrong-kind")]
    [InlineData("gap")]
    [InlineData("duplicate")]
    [InlineData("extra-value")]
    [InlineData("null")]
    public void TagsRequireOneCompleteTypedArray(string mutation)
    {
        var field = TagField("Item.A");
        var values = field.Owner.Values.ToList();
        values[0] = mutation switch
        {
            "wrong-type" => values[0] with { Type = "StrProperty" },
            "wrong-kind" => values[0] with { Kind = "string" },
            "gap" => values[0] with { Pointer = "/Properties/0/GameplayTags/1/TagName" },
            "null" => values[0] with { Value = null },
            _ => values[0]
        };
        if (mutation == "duplicate") values.Add(values[0]);
        if (mutation == "extra-value") values.Add(new("/Properties/0/Other", "FName", "name", "Item.B"));
        field = field with { Owner = field.Owner with { Values = values } };

        Assert.Throws<InvalidDataException>(() => PresentationQueryEvidence.Tags(field));
    }

    [Fact]
    public void MissingContainerTagsRemainTheEmptySet()
    {
        Assert.Empty(PresentationQueryEvidence.Tags(null));
    }

    [Fact]
    public void NativeAndTaggedQueryDictionariesDecodeTheSameNames()
    {
        var field = QueryField([0, 1, 1, 1, 0]);
        var owner = field.Owner with
        {
            Properties = field.Owner.Properties.Where(property => !property.Pointer.Contains("/Properties/0/0/") &&
                !property.Pointer.Contains("/Properties/0/1/")).ToArray(),
            Values = field.Owner.Values.Select(value => value.Pointer.Contains("/Properties/0/0/") || value.Pointer.Contains("/Properties/0/1/")
                ? value with { Pointer = value.Pointer.Replace("/Properties/0/0/Properties/0", "/Properties/0/0/TagName")
                    .Replace("/Properties/0/1/Properties/0", "/Properties/0/1/TagName"), Type = "FName" }
                : value).ToArray()
        };

        var query = PresentationQueryEvidence.Query(field with { Owner = owner });

        Assert.Equal(new[] { "Item.A", "Item.B" }, query.TagDictionary);
    }

    [Fact]
    public void DecodedEmptyTagArraysAreAcceptedButMissingArraysAreNot()
    {
        var field = TagField("Item.A");
        var empty = field with { Owner = field.Owner with
        {
            Values = [new("/Properties/0/GameplayTags", "FGameplayTag[]", "empty-array", null)]
        } };

        Assert.Empty(PresentationQueryEvidence.Tags(empty));
        Assert.Throws<InvalidDataException>(() => PresentationQueryEvidence.Tags(field with { Owner = field.Owner with { Values = [] } }));
    }

    [Theory]
    [InlineData("gap")]
    [InlineData("noncanonical")]
    [InlineData("missing-tag")]
    [InlineData("wrong-type")]
    [InlineData("wrong-version")]
    public void QueryDictionaryAndFieldsCannotBeReinterpreted(string mutation)
    {
        var field = QueryField([0, 1, 1, 1, 0]);
        var properties = field.Owner.Properties.ToList();
        var values = field.Owner.Values.ToList();
        switch (mutation)
        {
            case "gap": properties.RemoveAt(2); break;
            case "noncanonical": properties[2] = properties[2] with { Pointer = "/Properties/0/Properties/0/00/Properties/0" }; break;
            case "missing-tag": properties[2] = properties[2] with { Name = "Other" }; break;
            case "wrong-type": properties[1] = properties[1] with { Type = "StructProperty" }; break;
            case "wrong-version":
                properties.Add(new("/Properties/0/Properties/2", "TokenStreamVersion", "IntProperty", null, 1));
                values.Add(new("/Properties/0/Properties/2", "IntProperty", "integer", "1"));
                break;
        }
        field = field with { Owner = field.Owner with { Properties = properties, Values = values } };

        Assert.Throws<InvalidDataException>(() => PresentationQueryEvidence.Query(field));
    }

    private static EvidenceField TagField(string tag)
    {
        EvidenceProperty[] properties = [new("/Properties/0", "Tags", "StructProperty", null, 1)];
        var owner = new DecodedObject("/Game/Item.Item", "Container", properties,
            [new("/Properties/0/GameplayTags/0/TagName", "FName", "name", tag)], [], []);
        return new(owner, properties[0]);
    }

    internal static EvidenceField QueryField(byte[] tokens)
    {
        EvidenceProperty[] properties =
        [
            new("/Properties/0", "ItemsQuery", "StructProperty", null, 1),
            new("/Properties/0/Properties/0", "TagDictionary", "ArrayProperty", null, 1),
            new("/Properties/0/Properties/0/0/Properties/0", "TagName", "NameProperty", null, 1),
            new("/Properties/0/Properties/0/1/Properties/0", "TagName", "NameProperty", null, 1),
            new("/Properties/0/Properties/1", "QueryTokenStream", "ArrayProperty", null, 1)
        ];
        var owner = new DecodedObject("/Game/Slot.Slot", "Slot", properties,
        [
            new("/Properties/0/Properties/0/0/Properties/0", "NameProperty", "name", "Item.A"),
            new("/Properties/0/Properties/0/1/Properties/0", "NameProperty", "name", "Item.B"),
            new("/Properties/0/Properties/1", "ByteProperty[]", "binary-base64", Convert.ToBase64String(tokens))
        ], [], []);
        return new(owner, properties[0]);
    }
}
