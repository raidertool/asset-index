namespace PublishSnapshot.Tests;

public sealed partial class PublisherTests
{
    [Theory]
    [InlineData("id")]
    [InlineData("image")]
    [InlineData("mapping")]
    [InlineData("role")]
    [InlineData("resource-class")]
    public void ContradictoryGameEvidenceRejectsBeforeGitAndCorrectedInputCanBeRetried(string mutation)
    {
        switch (mutation)
        {
            case "id": ChangeJson("assets.json", rows => rows[0]!["id"] = "999999"); break;
            case "mapping": ChangeJson("coverage.json", row => row["discovery"]!["mappingSha256"] = new string('c', 64)); break;
            case "role": ChangeJson("assets.json", rows => rows[0]!["presentation"]!["candidates"]![0]!["role"] = "title"); break;
            case "resource-class":
                ChangeLines("discovery/objects.jsonl.gz", rows =>
                {
                    rows[1]!["class"] = "Actor";
                    rows[1]!["references"]![0]!["targetPath"] = "/Script/Fixture.Actor";
                });
                ChangeLines("discovery/exports.jsonl.gz", rows => rows[1] = ExportHeader(
                    "PioneerGame/Content/T_Test.uasset", 0, Texture, "Actor", "Object"));
                ChangeLines("discovery/registry.jsonl.gz", rows => rows[1]!["class"] = "Actor");
                break;
            case "image":
                const string other = "/Game/T_Other.T_Other";
                var file = AddUnownedImage(other, "Texture2D");
                ChangeJson("assets.json", rows =>
                {
                    rows[0]!["images"]![0]!["resource"] = other;
                    rows[0]!["images"]![0]!["file"] = file;
                });
                break;
        }
        AssertEvidenceRejectedBeforeGit();
        Directory.Delete(preview, recursive: true);
        WritePreview(preview, "New name");
        Assert.True(Publisher.Publish(preview, remote, NextExtractor, "456").Changed);
        Assert.False(Publisher.Publish(preview, remote, NextExtractor, "456").Changed);
    }
}
