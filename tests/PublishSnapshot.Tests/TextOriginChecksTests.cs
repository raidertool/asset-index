using System.IO.Compression;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace PublishSnapshot.Tests;

public sealed class TextOriginChecksTests : IDisposable
{
    private const string Source = "/Game/Item.Item";
    private const string Parent = "/Game/Template.Template";
    private const string Table = "/Game/Labels.Labels";
    private readonly string directory = Path.Combine(Path.GetTempPath(), "text-origin-tests-" + Guid.NewGuid().ToString("N"));

    [Theory]
    [InlineData(0u, false)]
    [InlineData(1u, false)]
    [InlineData(2u, true)]
    [InlineData(3u, true)]
    [InlineData(4u, false)]
    public void BaseReferenceUsesActualTextAndCultureInvariantFlag(uint flags, bool invariant)
    {
        var source = Object(Source);
        Text(source)["flags"] = flags;
        var candidate = Candidate();
        candidate["reference"]!["cultureInvariant"] = invariant;

        Validate(candidate, source);

        candidate["reference"]!["cultureInvariant"] = !invariant;
        Reject(candidate, source);
    }

    [Theory]
    [InlineData("Literal")]
    [InlineData(null)]
    public void NoneHistoryIsInvariantEvenWithoutAFlag(string? sourceText)
    {
        var source = Object(Source);
        Text(source)["history"] = "None";
        Text(source)["namespace"] = null;
        Text(source)["key"] = null;
        Text(source)["source"] = sourceText;
        var candidate = Candidate();
        candidate["reference"] = Reference("", "", sourceText ?? "", true);

        Validate(candidate, source);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void StringTableCanPrecedeItsUserAndKeysUseExactEscaping(bool invariant)
    {
        var source = StringTableUser("Name~/");
        Text(source)["flags"] = invariant ? 2 : 0;
        var candidate = Candidate();
        candidate["reference"] = Reference("UI", "Name~/", "Label", invariant);

        Validate(candidate, StringTable("Name~/"), source);
        Validate(candidate, source, StringTable("Name~/"));
    }

    [Fact]
    public void FirstOwnerFollowsTemplatesWithoutDependingOnStreamOrderOrPathCase()
    {
        var source = Object(Source, text: false, template: "/Game/Middle.Middle");
        var middle = Object("/Game/Middle.Middle", text: false, template: Parent.ToUpperInvariant());
        var owner = Object(Parent);
        var candidate = Candidate(definedAt: Parent.ToLowerInvariant());
        candidate["field"] = "displayname";
        candidate["sourceClass"] = "labelmetadata";

        Validate(candidate, owner, middle, source);
        Validate(candidate, source, middle, owner);
    }

    [Fact]
    public void ALocalEmptyTextOwnsTheFieldAndCannotBeReplacedByAnAncestor()
    {
        var source = Object(Source, template: Parent);
        Text(source)["key"] = "";
        Text(source)["source"] = "";
        var candidate = Candidate();
        candidate["reference"] = Reference("UI", "", "");
        Validate(candidate, source, Object(Parent));

        Reject(Candidate(definedAt: Parent), source, Object(Parent));
    }

    [Theory]
    [InlineData("namespace")]
    [InlineData("key")]
    [InlineData("source")]
    [InlineData("sourceClass")]
    [InlineData("field")]
    [InlineData("definedAt")]
    [InlineData("sourcePath")]
    public void ValidLookingCandidateChangesCannotSubstituteTextOrItsOwner(string field)
    {
        var candidate = Candidate();
        var other = Object(Parent);
        Text(other)["source"] = "Another label";
        if (field is "namespace" or "key" or "source") candidate["reference"]![field] = "Other";
        else candidate[field] = field switch
        {
            "definedAt" or "sourcePath" => Parent,
            "sourceClass" => "OtherMetadata",
            _ => "Description"
        };

        Reject(candidate, Object(Source), other);
    }

    [Theory]
    [InlineData("wrong-type")]
    [InlineData("static-array")]
    [InlineData("nonzero-array-index")]
    [InlineData("duplicate-name")]
    public void CandidateNeedsAnUnambiguousScalarTextProperty(string mutation)
    {
        var source = Object(Source);
        var header = source["properties"]![0]!;
        switch (mutation)
        {
            case "wrong-type": header["type"] = "NameProperty"; break;
            case "static-array": header["arraySize"] = 2; break;
            case "nonzero-array-index": header["arrayIndex"] = 1; break;
            case "duplicate-name":
                source["properties"]!.AsArray().Add(Header("/Properties/1", "displayname"));
                break;
        }

        Reject(Candidate(), source);
    }

    [Theory]
    [InlineData("duplicate-text")]
    [InlineData("other-scalar")]
    [InlineData("nested-text")]
    [InlineData("missing-text")]
    [InlineData("diagnostic")]
    [InlineData("unsupported-history")]
    [InlineData("null-base-source")]
    public void CandidateNeedsUnambiguousDecodedTextAtItsRootProperty(string mutation)
    {
        var source = Object(Source);
        switch (mutation)
        {
            case "duplicate-text": source["texts"]!.AsArray().Add(Text(source).DeepClone()); break;
            case "other-scalar": source["values"]!.AsArray().Add(new JsonObject { ["pointer"] = "/Properties/0" }); break;
            case "nested-text": Text(source)["pointer"] = "/Properties/0/Properties/0"; break;
            case "missing-text": source["texts"]!.AsArray().Clear(); break;
            case "diagnostic": source["issues"]!.AsArray().Add(new JsonObject { ["pointer"] = "/Properties/0" }); break;
            case "unsupported-history": Text(source)["history"] = "NamedFormat"; break;
            case "null-base-source": Text(source)["source"] = null; break;
        }

        Reject(Candidate(), source);
    }

    [Theory]
    [InlineData("cycle")]
    [InlineData("missing-object")]
    [InlineData("missing-edge")]
    [InlineData("duplicate-edge")]
    [InlineData("wrong-role")]
    [InlineData("error")]
    [InlineData("null-with-target")]
    [InlineData("null-end")]
    public void IncompleteOrContradictoryTemplateChainsCannotProveInheritance(string mutation)
    {
        var source = Object(Source, text: false, template: Parent);
        var parent = Object(Parent);
        var edge = source["references"]![0]!;
        switch (mutation)
        {
            case "cycle": edge["targetPath"] = Source.ToUpperInvariant(); break;
            case "missing-object": edge["targetPath"] = "/Game/Missing.Missing"; break;
            case "missing-edge": source["references"]!.AsArray().Clear(); break;
            case "duplicate-edge": source["references"]!.AsArray().Add(edge.DeepClone()); break;
            case "wrong-role": edge["role"] = "property"; break;
            case "error": edge["error"] = "Decode failed"; break;
            case "null-with-target": edge["isNull"] = true; break;
            case "null-end": edge["isNull"] = true; edge["targetPath"] = null; break;
        }

        Reject(Candidate(definedAt: Parent), source, parent);
    }

    [Theory]
    [InlineData("alias")]
    [InlineData("missing-table")]
    [InlineData("wrong-class")]
    [InlineData("key-case")]
    [InlineData("duplicate-key")]
    [InlineData("pointer")]
    [InlineData("namespace")]
    [InlineData("source")]
    public void StringTableBindingRequiresTheExactObjectKeyAndValue(string mutation)
    {
        var source = StringTableUser("Name");
        var table = StringTable("Name");
        switch (mutation)
        {
            case "alias": Text(source)["tableId"] = "Labels"; break;
            case "missing-table": Text(source)["tableId"] = "/Game/Other.Other"; break;
            case "wrong-class": table["class"] = "OtherAsset"; break;
            case "key-case": Text(source)["key"] = "name"; break;
            case "duplicate-key": table["tableEntries"]!.AsArray().Add(table["tableEntries"]![0]!.DeepClone()); break;
            case "pointer": table["tableEntries"]![0]!["pointer"] = "/StringTable/Other"; break;
            case "namespace": table["tableEntries"]![0]!["namespace"] = "Other"; break;
            case "source": table["tableEntries"]![0]!["source"] = "Other"; break;
        }

        Reject(Candidate(), source, table);
    }

    [Fact]
    public void NestedNamesDoNotShadowInheritedRootFieldsAndUnrelatedTextIsNotRetained()
    {
        var source = Object(Source, text: false, template: Parent);
        source["properties"]!.AsArray().Add(Header("/Properties/0", "Nested", "StructProperty"));
        source["properties"]!.AsArray().Add(Header("/Properties/0/Properties/0", "DisplayName"));
        var unrelated = Object("/Game/Other.Other");
        Text(unrelated)["history"] = "UnsupportedButUnused";

        Validate(Candidate(definedAt: Parent), source, unrelated, Object(Parent));
    }

    [Fact]
    public void DuplicateObjectPathsAreRejectedEvenWhenTheyDifferOnlyByCase()
    {
        Reject(Candidate(), Object(Source), Object(Source.ToUpperInvariant()));
    }

    private void Reject(JsonObject candidate, params JsonObject[] objects) =>
        Assert.Throws<InvalidDataException>(() => Validate(candidate, objects));

    private void Validate(JsonObject candidate, params JsonObject[] objects)
    {
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "objects.jsonl.gz");
        using (var file = File.Create(path))
        using (var gzip = new GZipStream(file, CompressionLevel.Fastest))
        using (var writer = new StreamWriter(gzip))
            foreach (var value in objects) writer.WriteLine(value.ToJsonString());
        var assets = new JsonArray(new JsonObject
        {
            ["presentation"] = new JsonObject { ["candidates"] = new JsonArray(candidate.DeepClone()) }
        });
        using var document = JsonDocument.Parse(assets.ToJsonString());
        TextOriginChecks.Validate(document.RootElement, SnapshotFile.Read(path));
    }

    private static JsonObject Candidate(string definedAt = Source) => new()
    {
        ["sourcePath"] = Source,
        ["sourceClass"] = "LabelMetadata",
        ["field"] = "DisplayName",
        ["definedAt"] = definedAt,
        ["reference"] = Reference("UI", "Name", "Label")
    };

    private static JsonObject Reference(string ns, string key, string source, bool invariant = false) => new()
    {
        ["namespace"] = ns,
        ["key"] = key,
        ["source"] = source,
        ["cultureInvariant"] = invariant
    };

    private static JsonObject Header(string pointer, string name, string type = "TextProperty") => new()
    {
        ["pointer"] = pointer,
        ["name"] = name,
        ["type"] = type,
        ["arrayIndex"] = null,
        ["arraySize"] = 1,
        ["serializeType"] = "Property"
    };

    private static JsonObject Object(string path, bool text = true, string? template = null) => new()
    {
        ["path"] = path,
        ["class"] = "LabelMetadata",
        ["properties"] = text ? new JsonArray(Header("/Properties/0", "DisplayName")) : new JsonArray(),
        ["texts"] = text ? new JsonArray(new JsonObject
        {
            ["pointer"] = "/Properties/0",
            ["flags"] = 0,
            ["history"] = "Base",
            ["namespace"] = "UI",
            ["key"] = "Name",
            ["source"] = "Label",
            ["tableId"] = null
        }) : new JsonArray(),
        ["references"] = new JsonArray(new JsonObject
        {
            ["pointer"] = "/Template",
            ["role"] = "template",
            ["targetPath"] = template,
            ["isNull"] = template is null,
            ["error"] = null
        }),
        ["values"] = new JsonArray(),
        ["issues"] = new JsonArray(),
        ["tableEntries"] = new JsonArray()
    };

    private static JsonNode Text(JsonObject source) => source["texts"]![0]!;

    private static JsonObject StringTableUser(string key)
    {
        var source = Object(Source);
        Text(source)["history"] = "StringTableEntry";
        Text(source)["namespace"] = null;
        Text(source)["source"] = null;
        Text(source)["key"] = key;
        Text(source)["tableId"] = Table;
        return source;
    }

    private static JsonObject StringTable(string key)
    {
        var table = Object(Table, text: false);
        table["class"] = "StringTable";
        table["tableEntries"]!.AsArray().Add(new JsonObject
        {
            ["pointer"] = "/StringTable/" + key.Replace("~", "~0").Replace("/", "~1"),
            ["namespace"] = "UI",
            ["key"] = key,
            ["source"] = "Label"
        });
        return table;
    }

    public void Dispose()
    {
        if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
    }
}
