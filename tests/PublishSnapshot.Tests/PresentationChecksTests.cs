using System.Text.Json;
using System.Text.Json.Nodes;

namespace PublishSnapshot.Tests;

public sealed class PresentationChecksTests
{
    private const string Definition = "/Game/Definition.Definition";
    private const string Metadata = "/Game/Metadata.Metadata";
    private const string Container = "/Game/Container.Container";
    private const string RootLabel = "/Game/RootLabel.RootLabel";

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void NpcSpecificNameOwnsTheSelectionEvenWhenEmpty(bool empty)
    {
        var generic = Candidate("metadata", "display-name", "Generic item");
        var npc = Candidate("metadata", "display-name", empty ? "" : "NPC name");
        npc["sourceClass"] = "UINPCMetaDataItem";
        npc["field"] = "DisplayName";
        var asset = Asset(generic, npc);
        asset["definitions"]![0]!["class"] = "NPCItemDataAsset";
        asset["presentation"]!["name"] = empty ? null : npc["reference"]!.DeepClone();
        Validate(asset);

        asset["presentation"]!["name"] = generic["reference"]!.DeepClone();
        Assert.Throws<InvalidDataException>(() => Validate(asset));
    }

    [Theory]
    [InlineData("SessionModifierDataAsset", true)]
    [InlineData("UnrelatedDataAsset", false)]
    public void OnlyModifierDefinitionsUseTypedDescriptionsAsLabels(string definition, bool label)
    {
        var description = Candidate("definition", "description", "Modifier label");
        description["sourceClass"] = "SessionModifierDataAsset";
        description["field"] = "Description";
        var asset = Asset(description);
        asset["definitions"]![0]!["class"] = definition;
        asset["presentation"]!["description"] = description["reference"]!.DeepClone();
        asset["presentation"]!["name"] = label ? description["reference"]!.DeepClone() : null;
        Validate(asset);

        asset["presentation"]!["name"] = label ? null : description["reference"]!.DeepClone();
        Assert.Throws<InvalidDataException>(() => Validate(asset));
    }

    [Fact]
    public void ModifierDescriptionDoesNotReplaceAnExplicitEmptyName()
    {
        var description = Candidate("definition", "description", "Modifier label");
        description["sourceClass"] = "SessionModifierDataAsset";
        description["field"] = "Description";
        var asset = Asset(description, Candidate("metadata", "display-name", ""));
        asset["definitions"]![0]!["class"] = "SessionModifierDataAsset";
        asset["presentation"]!["description"] = description["reference"]!.DeepClone();
        Validate(asset);

        asset["presentation"]!["name"] = description["reference"]!.DeepClone();
        Assert.Throws<InvalidDataException>(() => Validate(asset));
    }

    [Fact]
    public void RootCategoryCannotReplaceAnExplicitEmptyName()
    {
        var category = Candidate("inventory-root", "display-name", "Stash");
        var asset = Asset(Candidate("metadata", "display-name", ""), category);
        Validate(asset);

        asset["presentation"]!["name"] = category["reference"]!.DeepClone();
        Assert.Throws<InvalidDataException>(() => Validate(asset));
    }

    [Theory]
    [InlineData("metadata", "short-name", "definition", "display-name", "name")]
    [InlineData("definition", "short-name", "container", "display-name", "name")]
    [InlineData("container", "short-name", "visual-slot", "display-name", "name")]
    [InlineData("visual-slot", "short-name", "inventory-root", "display-name", "name")]
    [InlineData("metadata", "display-name", "inventory-root", "display-name", "name")]
    [InlineData("metadata", "display-name", "metadata", "title", "name")]
    [InlineData("metadata", "title", "metadata", "short-name", "name")]
    [InlineData("metadata", "tooltip", "definition", "description", "description")]
    [InlineData("definition", "description", "definition", "tooltip", "description")]
    public void SelectionUsesKindBeforeRoleAndRejectsALowerTier(string kind, string role, string lowerKind, string lowerRole, string field)
    {
        var preferred = Candidate(kind, role, "Preferred");
        var lower = Candidate(lowerKind, lowerRole, "Lower");
        var asset = Asset(lower, preferred);
        asset["presentation"]![field] = preferred["reference"]!.DeepClone();
        Validate(asset);

        asset["presentation"]![field] = lower["reference"]!.DeepClone();
        Assert.Throws<InvalidDataException>(() => Validate(asset));
    }

    [Theory]
    [InlineData("conflict", "name")]
    [InlineData("conflict", "description")]
    [InlineData("empty", "name")]
    [InlineData("empty", "description")]
    [InlineData("context-only", "name")]
    [InlineData("context-only", "description")]
    public void NullIsRequiredForConflictingEmptyOrInapplicableCandidates(string reason, string field)
    {
        var role = field == "name" ? "display-name" : "description";
        var first = Candidate("metadata", reason == "context-only" ? "unlock-description" : role, reason == "empty" ? "" : "First");
        var asset = reason == "conflict"
            ? Asset(first, Candidate("metadata", role, "Other"), Candidate("definition", role, "Fallback"))
            : reason == "empty" ? Asset(first, Candidate("definition", role, "Fallback")) : Asset(first);
        Validate(asset);

        asset["presentation"]![field] = first["reference"]!.DeepClone();
        Assert.Throws<InvalidDataException>(() => Validate(asset));
    }

    [Fact]
    public void NoCandidatesRequiresNullPrimaries()
    {
        var asset = Asset();
        Validate(asset);

        asset["presentation"]!["name"] = Candidate("definition", "display-name", "Invented")["reference"]!.DeepClone();
        Assert.Throws<InvalidDataException>(() => Validate(asset));
    }

    [Theory]
    [InlineData("namespace")]
    [InlineData("key")]
    [InlineData("source")]
    [InlineData("cultureInvariant")]
    public void ReferenceIdentityIncludesEveryField(string field)
    {
        var first = Candidate("definition", "display-name", "Name");
        var second = first.DeepClone();
        second["field"] = "OtherName";
        second["reference"]![field] = field == "cultureInvariant" ? JsonValue.Create(false) : JsonValue.Create("different");
        var asset = Asset(first, second);
        Validate(asset);

        asset["presentation"]!["name"] = first["reference"]!.DeepClone();
        Assert.Throws<InvalidDataException>(() => Validate(asset));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void IdenticalReferencesRequireTheirSinglePrimaryEvenWhenOnlyAKeyIsPresent(bool onlyKey)
    {
        var first = Candidate("metadata", "display-name", onlyKey ? "" : "Name");
        first["reference"]!["key"] = "name-key";
        var second = first.DeepClone();
        second["field"] = "OtherName";
        var asset = Asset(first, second);
        asset["presentation"]!["name"] = first["reference"]!.DeepClone();
        Validate(asset);

        asset["presentation"]!["name"] = null;
        Assert.Throws<InvalidDataException>(() => Validate(asset));
    }

    [Theory]
    [InlineData("definition", "metadata")]
    [InlineData("metadata", "definition")]
    [InlineData("container", "metadata")]
    [InlineData("metadata", "container")]
    [InlineData("visual-slot", "metadata")]
    [InlineData("metadata", "visual-slot")]
    [InlineData("inventory-root", "metadata")]
    [InlineData("metadata", "inventory-root")]
    [InlineData("container", "inventory-root")]
    [InlineData("inventory-root", "container")]
    public void CandidateOwnershipMustMatchItsDeclaredKind(string kind, string forgedKind)
    {
        var candidate = Candidate(kind, "display-name", "Name");
        candidate["sourceKind"] = forgedKind;
        var asset = Asset(candidate);
        asset["presentation"]!["name"] = candidate["reference"]!.DeepClone();

        Assert.Contains("declared source kind", Assert.Throws<InvalidDataException>(() => Validate(asset)).Message);
    }

    private static JsonObject Candidate(string kind, string role, string source) => new()
    {
        ["role"] = role,
        ["sourceKind"] = kind,
        ["sourcePath"] = kind == "definition" ? Definition : kind == "metadata" ? Metadata : kind == "inventory-root" ? RootLabel : Container,
        ["sourceClass"] = "Fixture",
        ["field"] = "Name",
        ["definedAt"] = Definition,
        ["reference"] = new JsonObject { ["namespace"] = "", ["key"] = "", ["source"] = source, ["cultureInvariant"] = true }
    };

    private static JsonObject Asset(params JsonNode[] candidates) => new()
    {
        ["definitions"] = new JsonArray(new JsonObject { ["path"] = Definition, ["class"] = "Fixture" }),
        ["metadata"] = new JsonArray(new JsonObject { ["path"] = Metadata, ["class"] = "Fixture" }),
        ["presentation"] = new JsonObject
        {
            ["name"] = null,
            ["description"] = null,
            ["candidates"] = new JsonArray(candidates.Select(candidate => candidate.DeepClone()).ToArray()),
            ["inventoryRoots"] = new JsonArray(new JsonObject
            {
                ["role"] = "container-slot",
                ["containerType"] = "ENewInventoryContainerType::Stash",
                ["rootPath"] = Container,
                ["rootField"] = "StashSlot",
                ["slotPath"] = Definition,
                ["containerPath"] = null,
                ["metadataPath"] = RootLabel
            }),
            ["visualSlots"] = new JsonArray(new JsonObject
            {
                ["slotPath"] = Definition,
                ["typeTag"] = "UI.Category",
                ["metadataPath"] = Container,
                ["members"] = new JsonArray(new JsonObject { ["itemPath"] = Definition, ["metadataPath"] = Metadata })
            }),
            ["containers"] = new JsonArray(new JsonObject
            {
                ["role"] = "container-slot",
                ["containerType"] = "Slot",
                ["framePath"] = Container,
                ["containerIndex"] = 0,
                ["slotPath"] = Definition,
                ["containerPath"] = null,
                ["metadataPath"] = Container
            })
        }
    };

    private static void Validate(JsonNode asset)
    {
        using var json = JsonDocument.Parse(asset.ToJsonString());
        PresentationChecks.Validate(json.RootElement, [Definition, Metadata], new HashSet<string> { Definition, Metadata, Container, RootLabel }.Contains);
    }
}

public sealed partial class PublisherTests
{
    [Fact]
    public void ConsistentAmbiguousTextRetainsCandidatesAndStillRequiresSuccessfulExtraction()
    {
        const string other = "/Game/OtherMetadata.OtherMetadata";
        ChangeJson("assets.json", rows =>
        {
            var metadata = rows[0]!["metadata"]![0]!.DeepClone();
            metadata["name"] = "OtherMetadata";
            metadata["path"] = other;
            rows[0]!["metadata"]!.AsArray().Add(metadata);
            var image = rows[0]!["images"]![0]!.DeepClone();
            image["source"] = other;
            rows[0]!["images"]!.AsArray().Add(image);
            var presentation = rows[0]!["presentation"]!;
            var candidate = presentation["candidates"]![0]!.DeepClone();
            candidate["sourcePath"] = other;
            candidate["definedAt"] = other;
            candidate["reference"]!["source"] = "Other name";
            presentation["candidates"]!.AsArray().Add(candidate);
            var description = presentation["candidates"]![1]!.DeepClone();
            description["sourcePath"] = other;
            description["definedAt"] = other;
            presentation["candidates"]!.AsArray().Add(description);
            presentation["name"] = null;
            rows[0]!["text"]![0]!["displayName"] = "";
        });
        ChangeJson("coverage.json", coverage =>
        {
            coverage["englishNames"] = 0;
            coverage["discovery"]!["objects"] = 4;
        });
        ChangeLines("discovery/objects.jsonl.gz", rows =>
        {
            var metadata = rows[0]!.DeepClone();
            metadata["path"] = other;
            metadata["texts"]![0]!["source"] = "Other name";
            rows.Add(metadata);
        });
        AddUnindexedPackage("PioneerGame/Content/OtherMetadata.uasset", loaded: true, exports: 1);
        ChangeLines("discovery/exports.jsonl.gz", rows => rows.Add(ExportHeader(
            "PioneerGame/Content/OtherMetadata.uasset", 0, other, "UIGameplayItemMetaDataItem", "UIMetaDataItem", "Object")));

        using var captured = Preview.Read(preview);
        using var assets = Preview.ReadJson(captured.Files, "assets.json");
        var presentation = assets.RootElement[0].GetProperty("presentation");
        Assert.Equal(JsonValueKind.Null, presentation.GetProperty("name").ValueKind);
        Assert.Equal(2, presentation.GetProperty("candidates").EnumerateArray().Count(candidate => candidate.GetProperty("role").GetString() == "display-name"));

        ChangeJson("coverage.json", coverage => coverage["issues"]!.AsArray().Add(new JsonObject { ["message"] = "Unrelated extraction failure" }));
        AssertRejected();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void TextSelectionMismatchIsRejectedBeforeGitAccess(bool conflicting)
    {
        ChangeJson("assets.json", rows =>
        {
            var presentation = rows[0]!["presentation"]!;
            if (conflicting)
            {
                var candidate = presentation["candidates"]![0]!.DeepClone();
                candidate["reference"]!["source"] = "Conflicting name";
                presentation["candidates"]!.AsArray().Add(candidate);
            }
            else
            {
                presentation["name"] = null;
                rows[0]!["text"]![0]!["displayName"] = "";
            }
        });
        if (!conflicting) ChangeJson("coverage.json", coverage => coverage["englishNames"] = 0);
        var references = remoteGit.Run("show-ref");

        var error = Assert.Throws<InvalidDataException>(() => Publisher.Publish(preview,
            Path.Combine(root, "nonexistent-remote"), NextExtractor, "456"));

        Assert.Contains("candidate precedence", error.Message);
        Assert.Equal(references, remoteGit.Run("show-ref"));
    }
}
