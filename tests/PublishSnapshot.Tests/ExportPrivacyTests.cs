using System.Text.Json.Nodes;

namespace PublishSnapshot.Tests;

public sealed partial class PublisherTests
{
    [Theory]
    [InlineData("assets.json", "/0")]
    [InlineData("assets.json", "/0/definitions/0")]
    [InlineData("assets.json", "/0/metadata/0")]
    [InlineData("assets.json", "/0/text/0")]
    [InlineData("assets.json", "/0/images/0")]
    [InlineData("assets.json", "/0/presentation")]
    [InlineData("assets.json", "/0/presentation/name")]
    [InlineData("assets.json", "/0/presentation/description")]
    [InlineData("assets.json", "/0/presentation/candidates/0")]
    [InlineData("assets.json", "/0/presentation/candidates/0/reference")]
    [InlineData("resources.json", "/0")]
    public void ExportRejectsUnapprovedFieldsAtEveryPublishedObjectLevel(string file, string pointer)
    {
        ChangeJson(file, root =>
        {
            var node = root;
            foreach (var part in pointer.Split('/', StringSplitOptions.RemoveEmptyEntries))
                node = (int.TryParse(part, out var index) ? node[index] : node[part])!;
            node.AsObject().Add("privateApiId", "private-value");
        });
        var output = Path.Combine(root, "safe-export");

        var error = Assert.Throws<InvalidDataException>(() => Publisher.Export(preview, output, NextExtractor, "456"));

        Assert.Contains("JSON fields do not match", error.Message);
        Assert.False(Path.Exists(output));
    }
}
