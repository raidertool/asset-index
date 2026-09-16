using System.Text.Json;
using System.Text.Json.Nodes;

namespace PublishSnapshot.Tests;

public sealed partial class PublisherTests
{
    [Theory]
    [InlineData("missing-properties")]
    [InlineData("empty-properties")]
    [InlineData("non-array")]
    [InlineData("missing-field")]
    [InlineData("extra-field")]
    [InlineData("empty-name")]
    [InlineData("empty-type")]
    [InlineData("duplicate-pointer")]
    [InlineData("leading-zero")]
    [InlineData("negative-ordinal")]
    [InlineData("overflow-ordinal")]
    [InlineData("named-pointer")]
    [InlineData("wrong-container")]
    [InlineData("gap")]
    [InlineData("nested-gap")]
    [InlineData("bad-escape")]
    [InlineData("dangling-parent")]
    [InlineData("negative-index")]
    [InlineData("zero-size")]
    [InlineData("index-outside-size")]
    [InlineData("fractional-index")]
    [InlineData("unknown-serialize-type")]
    [InlineData("dangling-value")]
    [InlineData("dangling-text")]
    [InlineData("dangling-reference")]
    [InlineData("dangling-nested-value")]
    public void InvalidPropertyHeadersRejectBeforeGitRemoteAccess(string mutation)
    {
        ChangeLines("discovery/objects.jsonl.gz", rows => MutateProperties(rows[0]!.AsObject(), mutation));
        var references = remoteGit.Run("show-ref");
        var publishedFiles = remoteGit.Run("ls-tree", "-r", "refs/heads/data");
        var input = HashFiles(preview);

        // A missing Git remote would raise IOException if validation reached Git.
        Assert.Throws<InvalidDataException>(() => Publisher.Publish(preview,
            Path.Combine(root, "nonexistent-remote.git"), NextExtractor, "456"));

        Assert.Equal(references, remoteGit.Run("show-ref"));
        Assert.Equal(publishedFiles, remoteGit.Run("ls-tree", "-r", "refs/heads/data"));
        Assert.Equal(input, HashFiles(preview));
    }

    [Fact]
    public void PositionalHeadersAllowRepeatedNamesAndRecordedNestedContainers()
    {
        var headers = new JsonArray
        {
            PropertyHeader("/Properties/0", "Shared", "StructProperty"),
            PropertyHeader("/Properties/1", "Items", "ArrayProperty"),
            PropertyHeader("/Properties/2", "Shared", arrayIndex: 0, arraySize: 2),
            PropertyHeader("/Properties/3", "Shared", arrayIndex: 1, arraySize: 2, serializeType: "BinaryOrNative"),
            PropertyHeader("/Properties/0/Properties/0", "Name~/", serializeType: "Skipped"),
            PropertyHeader("/Properties/1/0/Properties/0", "ArrayEntry"),
            PropertyHeader("/Rows/Row~1Name/Properties/0", "Value")
        };
        using var document = JsonDocument.Parse(headers.ToJsonString());
        var properties = PropertyHeaders.Read(document.RootElement);

        foreach (var pointer in new[] { "/Properties/0/Properties/0", "/Properties/1", "/Properties/2", "/Properties/3",
            "/Properties/1/0/Properties/0", "/Rows/Row~1Name/Properties/0", "/Native/Properties/LiteralMember",
            "/Properties/0/Native/Properties/LiteralMember", "/Properties", "/StringTable/Name~0Key" })
            properties.ValidatePointer(pointer);
    }

    private static void MutateProperties(JsonObject row, string mutation)
    {
        var headers = row["properties"]!.AsArray();
        var first = headers[0]!.AsObject();
        switch (mutation)
        {
            case "missing-properties": row.Remove("properties"); break;
            case "empty-properties": headers.Clear(); break;
            case "non-array": row["properties"] = new JsonObject(); break;
            case "missing-field": first.Remove("name"); break;
            case "extra-field": first["unexpected"] = true; break;
            case "empty-name": first["name"] = ""; break;
            case "empty-type": first["type"] = ""; break;
            case "duplicate-pointer": headers.Add(first.DeepClone()); break;
            case "leading-zero": first["pointer"] = "/Properties/00"; break;
            case "negative-ordinal": first["pointer"] = "/Properties/-1"; break;
            case "overflow-ordinal": first["pointer"] = "/Properties/2147483648"; break;
            case "named-pointer": first["pointer"] = "/Properties/AssetId"; break;
            case "wrong-container": first["pointer"] = "/Other/0"; break;
            case "gap": headers.RemoveAt(0); break;
            case "nested-gap": headers.Add(PropertyHeader("/Properties/0/Properties/1")); break;
            case "bad-escape": headers.Add(PropertyHeader("/Rows/Bad~2Escape/Properties/0")); break;
            case "dangling-parent": headers.Add(PropertyHeader("/Properties/99/Properties/0")); break;
            case "negative-index": first["arrayIndex"] = -1; break;
            case "zero-size": first["arraySize"] = 0; break;
            case "index-outside-size": first["arrayIndex"] = 2; first["arraySize"] = 2; break;
            case "fractional-index": first["arrayIndex"] = 0.5; break;
            case "unknown-serialize-type": first["serializeType"] = "Unknown"; break;
            case "dangling-value": row["values"]!.AsArray().Add(new JsonObject
            { ["pointer"] = "/Properties/99", ["type"] = "Int64Property", ["kind"] = "integer", ["value"] = "42" }); break;
            case "dangling-text": row["texts"]![0]!["pointer"] = "/Properties/99"; break;
            case "dangling-reference": row["references"]![0]!["pointer"] = "/Properties/99"; break;
            case "dangling-nested-value":
                headers.Add(PropertyHeader("/Properties/0/Properties/0"));
                row["values"]!.AsArray().Add(new JsonObject
                { ["pointer"] = "/Properties/0/Properties/1", ["type"] = "Int64Property", ["kind"] = "integer", ["value"] = "42" });
                break;
            default: throw new ArgumentOutOfRangeException(nameof(mutation));
        }
    }
}
