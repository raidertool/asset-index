using AssetIndex.Discovery;
using CUE4Parse.UE4.Assets.Objects;

namespace AssetIndex.Tests;

public sealed class DiscoveryCollisionTests
{
    [Theory]
    [InlineData("Actual", "Actual")]
    [InlineData("Actual", "actual")]
    public void DuplicatePathsKeepBothOriginsAndTheSecondExportsDiagnostics(string firstName, string secondName)
    {
        const string path = "Plugin/Selected.uasset";
        var package = new CrawlerPackage(path, "/Plugin/Selected", new CrawlerExport(firstName), new CrawlerExport(secondName)
        {
            OnLoad = value => value.Properties.Add(new FPropertyTag { Name = "UnreadField", Tag = null })
        });
        using var provider = new CrawlerProvider(package);
        var evidence = new List<ObjectEvidence>();

        var result = new ObjectCrawler(provider, evidence.Add, _ => { }).Read([], (_, _) => { });

        var collision = Assert.Single(result.Issues, issue => issue.Message.StartsWith("Duplicate object path"));
        Assert.Equal("decode", collision.Stage);
        Assert.Equal($"{path}#export/1", collision.Path);
        Assert.Contains($"first decoded at {path}#export/0", collision.Message);
        Assert.Contains($"repeated at {path}#export/1", collision.Message);
        Assert.Equal(2, evidence.Count);
        Assert.Contains(result.Issues, issue => issue.Stage == "evidence" && issue.Path.EndsWith("/Properties/0"));
        Assert.Equal("incomplete", Assert.Single(result.Packages).Status);
    }

    [Fact]
    public void DistinctPathsRemainSeparateWithoutCollisionDiagnostics()
    {
        using var provider = new CrawlerProvider(new CrawlerPackage("Plugin/Selected.uasset", "/Plugin/Selected", new CrawlerExport("First"), new CrawlerExport("Second")));
        var evidence = new List<ObjectEvidence>();

        var result = new ObjectCrawler(provider, evidence.Add, _ => { }).Read([], (_, _) => { });

        Assert.Empty(result.Issues);
        Assert.Equal(2, evidence.Count);
    }

    [Fact]
    public void RepeatedObjectFromAnotherInputRecordsBothPhysicalOrigins()
    {
        const string first = "Plugin/Selected.uasset";
        const string second = "Plugin/Alias.uasset";
        using var provider = new CrawlerProvider(new CrawlerPackage(first, "/Plugin/Selected", new CrawlerExport("Actual")),
            new CrawlerPackage(second, "/Plugin/Selected", new CrawlerExport("Actual")));

        var result = new ObjectCrawler(provider, _ => { }, _ => { }).Read([], (_, _) => { });

        var collision = Assert.Single(result.Issues, issue => issue.Message.StartsWith("Duplicate object path"));
        Assert.Contains($"first decoded at {second}#export/0", collision.Message);
        Assert.Contains($"repeated at {first}#export/0", collision.Message);
    }
}
