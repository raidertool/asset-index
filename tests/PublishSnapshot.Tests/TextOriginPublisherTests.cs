namespace PublishSnapshot.Tests;

public sealed partial class PublisherTests
{
    [Theory]
    [InlineData("reference")]
    [InlineData("sourceClass")]
    [InlineData("field")]
    [InlineData("definedAt")]
    public void UnprovenTextOriginFailsBeforeRemoteAccess(string mutation)
    {
        ChangeJson("assets.json", rows =>
        {
            var asset = rows[0]!;
            var candidate = asset["presentation"]!["candidates"]![0]!;
            if (mutation == "reference")
            {
                // Selection and rendered text agree; only the game evidence contradicts them.
                candidate["reference"]!["source"] = "Invented label";
                asset["presentation"]!["name"]!["source"] = "Invented label";
                asset["text"]![0]!["displayName"] = "Invented label";
            }
            else candidate[mutation] = mutation switch
            {
                "sourceClass" => "OtherMetadata",
                "field" => "Description",
                "definedAt" => Texture,
                _ => throw new ArgumentOutOfRangeException(nameof(mutation))
            };
        });
        var before = remoteGit.Run("show-ref");

        var error = Assert.Throws<InvalidDataException>(() => Publisher.Publish(preview,
            Path.Combine(root, "nonexistent-remote"), NextExtractor, "456"));

        Assert.Contains("Text candidate", error.Message);
        Assert.Equal(before, remoteGit.Run("show-ref"));
    }
}
