using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using AssetIndex;
using CUE4Parse.UE4.IO.Objects;

namespace PublishSnapshot.Tests;

public sealed partial class PublisherTests
{
    private const string UnavailableSource = "/Game/Exploration.Exploration";
    private const string UnavailableFile = "PioneerGame/Content/Exploration.uasset";
    private const string AbsentTarget = "/Game/Absent.Absent";

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ProvedUnavailableOrdinaryReferencesKeepCatalogAndPrivateEvidence(bool hard)
    {
        AddUnavailableFixture(hard);
        var original = HashFiles(preview);

        var result = Publisher.Publish(preview, remote, NextExtractor, "456");

        Assert.True(result.Changed);
        Assert.Equal(original, HashFiles(preview));
        using var coverage = JsonDocument.Parse(remoteGit.Run("show", "refs/heads/main:coverage.json"));
        Assert.Equal(3, coverage.RootElement.GetProperty("formatVersion").GetInt32());
        Assert.Equal(1, coverage.RootElement.GetProperty("exploration").GetProperty(hard ? "unavailableHardReferences" : "unavailableSoftReferences").GetInt32());
        Assert.DoesNotContain("discovery/", remoteGit.Run("ls-tree", "-r", "refs/heads/main"));
        Assert.DoesNotContain(AbsentTarget, coverage.RootElement.GetRawText());
    }

    [Theory]
    [InlineData("PersistenceDataAsset")]
    [InlineData("Icon")]
    [InlineData("MapAreas")]
    [InlineData("DefaultContainer")]
    [InlineData("AllowedContainersQuery")]
    [InlineData("AugmentSlot")]
    public void ProtectedFieldsCannotUseUnavailableExceptions(string field)
    {
        AddUnavailableFixture(false);
        ChangeLines("discovery/objects.jsonl.gz", rows => Extra(rows)["properties"]![0]!["name"] = field);

        RejectUnavailable("Catalog dependency cannot be unavailable");
    }

    [Theory]
    [InlineData("/Class", "class")]
    [InlineData("/Template", "template")]
    [InlineData("/Native/ClassDefaultObject", "class-default")]
    public void StructuralReferencesCannotUseUnavailableExceptions(string pointer, string role)
    {
        AddUnavailableFixture(false);
        ChangeLines("discovery/objects.jsonl.gz", rows =>
        {
            var edge = MissingEdge(rows);
            edge["pointer"] = pointer;
            edge["role"] = role;
        });
        RejectUnavailable("ordinary nonnull property");
    }

    [Theory]
    [InlineData("normal")]
    [InlineData("optional")]
    [InlineData("redirects")]
    [InlineData("packageChunks")]
    [InlineData("localized")]
    public void AnIndexedTargetCannotBeDeclaredUnavailable(string kind)
    {
        AddUnavailableFixture(true);
        ChangePackageIndex(node =>
        {
            var rows = node["containers"]![0]![kind]!.AsArray();
            var target = PackageId(AbsentTarget);
            rows.Add(kind switch
            {
                "normal" or "optional" => new JsonObject { ["id"] = target, ["imports"] = new JsonArray() },
                "redirects" => new JsonObject { ["source"] = target, ["target"] = PackageId(UnavailableSource) },
                _ => JsonValue.Create(target)
            });
        });
        RejectUnavailable("present package identity");
    }

    [Theory]
    [InlineData("map")]
    [InlineData("hash")]
    [InlineData("package")]
    [InlineData("binding")]
    [InlineData("unmounted")]
    public void InvalidOrIncompleteHardImportEvidenceCannotAuthorizeAbsence(string change)
    {
        AddUnavailableFixture(true);
        if (change == "map")
            ChangeLines("discovery/objects.jsonl.gz", rows => MissingEdge(rows)["packageIndex"] = -2);
        else ChangePackageIndex(node =>
        {
            switch (change)
            {
                case "hash": node["imports"]![0]!["exportHashes"] = new JsonArray(); break;
                case "package": node["imports"]![0]!["importMap"]![0] = "8000000100000000"; break;
                case "binding": node["containers"]![0]!["normal"]![0]!["imports"]![0] = "0000000000000001"; break;
                case "unmounted": node["containers"]![0]!["mounted"] = false; break;
            }
        });
        RejectUnavailable();
    }

    [Fact]
    public void ASecondCriticalEdgeToTheSameMissingPackageStillBlocks()
    {
        AddUnavailableFixture(false);
        ChangeLines("discovery/objects.jsonl.gz", rows =>
        {
            var reference = rows[0]!["references"]!.AsArray().Single(row => row!["pointer"]!.GetValue<string>() == "/Properties/3")!;
            reference["targetPath"] = AbsentTarget;
        });
        RejectUnavailable("Referenced package was not inspected");
    }

    [Fact]
    public void MissingEvidenceAndOmittedFailureEdgesCannotPass()
    {
        AddUnavailableFixture(true);
        ChangeLines("discovery/objects.jsonl.gz", rows => Extra(rows)["references"]!.AsArray().Remove(MissingEdge(rows)));
        RejectUnavailable("no unavailable reference");
    }

    [Fact]
    public void MountedLogicalPackageNamesRejectAbsenceEvenWithoutIoStoreIndexes()
    {
        AddUnavailableFixture(false);
        ChangeLines("discovery/packages.jsonl.gz", rows => rows[0]!["name"] = "/Game/Absent");
        RejectUnavailable("present package identity");
    }

    [Fact]
    public void UnknownClassEligibilityStillBlocksWithAnUnavailableReference()
    {
        AddUnavailableFixture(false);
        ChangeLines("discovery/objects.jsonl.gz", rows => Extra(rows)["class"] = "MissingNativeClass");
        RejectUnavailable();
    }

    [Fact]
    public void AbsenceCoverageMustMatchTheExactReferenceEdges()
    {
        AddUnavailableFixture(false);
        ChangeJson("coverage.json", node => node["discovery"]!["unavailableSoftReferences"] = 2);
        RejectUnavailable("coverage mismatch");
    }

    [Fact]
    public void UnavailableInputsRequireTheIndependentContainerCensus()
    {
        AddUnavailableFixture(true);
        ChangeJson("coverage.json", node => node["discovery"]!["inputContainers"] = null);
        RejectUnavailable("independent container census");
    }

    [Fact]
    public void DroppingAnOptionalStoreEntryCannotHideAnAvailablePackage()
    {
        AddUnavailableFixture(true);
        ChangeJson("coverage.json", node => node["discovery"]!["inputContainers"]![0]!["optionalPackages"] = 1);
        RejectUnavailable("optionalPackages");
    }

    [Fact]
    public void DroppingAWholeInputContainerCannotProveAbsence()
    {
        AddUnavailableFixture(false);
        ChangeJson("coverage.json", node => node["discovery"]!["inputContainers"]!.AsArray().Add(new JsonObject
        {
            ["path"] = "omitted.utoc",
            ["mounted"] = true,
            ["normalPackages"] = 1,
            ["optionalPackages"] = 0,
            ["packageChunks"] = 1,
            ["redirects"] = 0,
            ["localizedPackages"] = 0
        }));
        RejectUnavailable("census differs");
    }

    [Fact]
    public void WhitespaceInAHardPackageIdentityCannotBypassThePresentSet()
    {
        AddUnavailableFixture(true);
        const string padded = "              42";
        ChangePackageIndex(node =>
        {
            node["containers"]![0]!["packageChunks"]!.AsArray().Add("0000000000000042");
            node["containers"]![0]!["normal"]![0]!["imports"]![0] = padded;
        });
        ChangeLines("discovery/objects.jsonl.gz", rows => MissingEdge(rows)["unavailable"]!["packageId"] = padded);
        RejectUnavailable("canonical hexadecimal");
    }

    [Fact]
    public void InvalidPackageSentinelCannotBecomeAnUnavailableInput()
    {
        AddUnavailableFixture(true);
        ChangePackageIndex(node => node["containers"]![0]!["normal"]![0]!["imports"]![0] = "ffffffffffffffff");
        ChangeLines("discovery/objects.jsonl.gz", rows => MissingEdge(rows)["unavailable"]!["packageId"] = "ffffffffffffffff");
        RejectUnavailable("Invalid package ID");
    }

    [Fact]
    public void PackageChunksWithoutAContainerHeaderCannotProveAbsence()
    {
        AddUnavailableFixture(true);
        ChangePackageIndex(node => node["containers"]![0]!["hasHeader"] = false);
        RejectUnavailable("no header");
    }

    [Fact]
    public void ProtectedFieldPolicyCoversEveryPublisherConsumer()
    {
        var fields = IdentityContext.RootFields.Concat(PresentationRelationChecks.RootFields)
            .Concat(TextRoleChecks.RootFields).Concat(ImageOriginChecks.RootFields);
        Assert.All(fields, field => Assert.Contains(field, ReferencePolicy.ProtectedFields));
    }

    private void RejectUnavailable(string? message = null)
    {
        var refs = remoteGit.Run("show-ref");
        var files = remoteGit.Run("ls-tree", "-r", "refs/heads/main");
        var error = Assert.ThrowsAny<Exception>(() => Publisher.Publish(preview, remote, NextExtractor, "456"));
        if (message is not null) Assert.Contains(message, error.Message);
        Assert.Equal(refs, remoteGit.Run("show-ref"));
        Assert.Equal(files, remoteGit.Run("ls-tree", "-r", "refs/heads/main"));
    }

    private void AddUnavailableFixture(bool hard)
    {
        var type = hard ? "ItemTransmutationTableDataAsset" : "PreloadableEmbarkActorListAsset";
        var pointer = hard ? "/Properties/0/0/Properties/0" : "/Properties/0/0";
        var field = hard ? "DirectItemTransforms" : "WorldItemClasses";
        ChangeLines("discovery/objects.jsonl.gz", rows =>
        {
            var properties = new JsonArray(PropertyHeader("/Properties/0", field, "ArrayProperty"));
            if (hard) properties.Add(PropertyHeader(pointer, "OutputItem", "ObjectProperty"));
            var references = JsonSerializer.SerializeToNode(ObjectLinks(type))!.AsArray();
            references.Add(new JsonObject
            {
                ["pointer"] = pointer,
                ["kind"] = hard ? "hard" : "soft",
                ["role"] = "property",
                ["targetPath"] = hard ? null : AbsentTarget,
                ["isNull"] = false,
                ["package"] = "/Game/Exploration",
                ["packageIndex"] = hard ? -1 : null,
                ["exportIndex"] = null,
                ["error"] = hard ? "Referenced package unavailable." : null,
                ["unavailable"] = new JsonObject { ["packageId"] = PackageId(AbsentTarget), ["ownerFile"] = hard ? UnavailableFile : null }
            });
            rows.Add(new JsonObject
            {
                ["path"] = UnavailableSource,
                ["class"] = type,
                ["properties"] = properties,
                ["references"] = references,
                ["texts"] = new JsonArray(),
                ["values"] = new JsonArray(),
                ["tableEntries"] = new JsonArray(),
                ["issues"] = new JsonArray()
            });
        });
        ChangeLines("discovery/files.jsonl.gz", rows => rows.Add(new JsonObject { ["path"] = UnavailableFile, ["registryPackages"] = new JsonArray() }));
        ChangeLines("discovery/packages.jsonl.gz", rows => rows.Add(new JsonObject
        {
            ["path"] = UnavailableFile,
            ["name"] = "/Game/Exploration",
            ["reason"] = "inventory",
            ["status"] = "succeeded",
            ["exports"] = 1,
            ["selected"] = new JsonArray(0),
            ["decoded"] = new JsonArray(0)
        }));
        ChangeLines("discovery/exports.jsonl.gz", rows => rows.Add(ExportHeader(UnavailableFile, 0, UnavailableSource, type,
            hard ? ["DataAsset", "Object"] : ["PrimaryDataAsset", "DataAsset", "Object"])));
        ChangeJson("coverage.json", node =>
        {
            node["candidates"] = 4; node["loaded"] = 4;
            node["discovery"]!["objects"] = 4;
            node["discovery"]!["unavailableSoftReferences"] = hard ? 0 : 1;
            node["discovery"]!["unavailableHardReferences"] = hard ? 1 : 0;
            node["discovery"]!["unmappedNonCatalogExports"] = 0;
        });
        var ownId = PackageId(UnavailableSource);
        var record = hard ? new PackageIndexRecord(
            [new("fixture.utoc", true, true, [ownId], [new(ownId, [PackageId(AbsentTarget)])], [], [], [])],
            [new(ownId, UnavailableFile, "fixture.utoc")],
            [new(UnavailableFile, "/Game/Exploration", "fixture.utoc", ownId, ["8000000000000000"], ["0000000000000042"])])
            : new([], [], []);
        WriteLines(preview, "discovery/package-index.jsonl.gz", new[] { JsonSerializer.SerializeToNode(record, Preview.Json) });
        ChangeJson("coverage.json", node => node["discovery"]!["inputContainers"] = new JsonArray(record.Containers.Select(container =>
            (JsonNode)new JsonObject
            {
                ["path"] = container.Path,
                ["mounted"] = container.Mounted,
                ["normalPackages"] = container.Normal.Count,
                ["optionalPackages"] = container.Optional.Count,
                ["packageChunks"] = container.PackageChunks.Count,
                ["redirects"] = container.Redirects.Count,
                ["localizedPackages"] = container.Localized.Count
            }).ToArray()));
    }

    private void ChangePackageIndex(Action<JsonNode> change) =>
        ChangeLines("discovery/package-index.jsonl.gz", rows => change(rows[0]!));

    private static JsonNode Extra(IEnumerable<JsonNode?> rows) => rows.Single(row => row!["path"]!.GetValue<string>() == UnavailableSource)!;
    private static JsonNode MissingEdge(IEnumerable<JsonNode?> rows) => Extra(rows)["references"]!.AsArray().Single(row => row!["unavailable"] is not null)!;
    private static string PackageId(string path) => FPackageId.FromName(path.Split('.', 2)[0]).id.ToString("x16", CultureInfo.InvariantCulture);
}
