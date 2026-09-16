using AssetIndex.Discovery;

namespace AssetIndex.Tests;

public sealed class ReferenceClosureTests
{
    [Fact]
    public void ALoadedPackageDoesNotSatisfyAMissingExport()
    {
        var references = new ReferenceClosure();
        references.Require("/Game/Source.Source/Properties/Icon", "/Game/Target.Missing");

        var issue = Assert.Single(references.Check(["/Game/Target.Existing"]));

        Assert.Equal("reference", issue.Stage);
        Assert.Equal("/Game/Source.Source/Properties/Icon", issue.Path);
        Assert.Contains("/Game/Target.Missing", issue.Message);
    }

    [Theory]
    [InlineData("/Game/Target.Target:Child", true)]
    [InlineData("/game/target.target:child", true)]
    [InlineData("/Game/Target.Other:Child", false)]
    [InlineData("/Game/Target.Target:Parent.Child", false)]
    public void SubobjectsRequireTheEntireOuterChain(string decoded, bool found)
    {
        var references = new ReferenceClosure();
        references.Require("source", "/Game/Target.Target:Child");

        Assert.Equal(found, !references.Check([decoded]).Any());
    }

    [Fact]
    public void PackageOnlyReferencesDoNotRequireAnExport()
    {
        var references = new ReferenceClosure();
        references.Require("source/Outer", "/Game/Target");

        Assert.Empty(references.Check([]));
    }

    [Fact]
    public void MultipleReferencesReportOneMissingTargetWithItsFirstSource()
    {
        var references = new ReferenceClosure();
        references.Require("first", "/Game/Target.Missing");
        references.Require("second", "/game/target.missing");

        Assert.Equal("first", Assert.Single(references.Check([])).Path);
    }

    [Fact]
    public void RequiredBodiesCannotBeSatisfiedByHeadersBeforeOrAfterPromotion()
    {
        var references = new ReferenceClosure();
        const string target = "/Game/Target.Default";
        Assert.True(references.Require("ordinary", target));
        references.Inventory(target);
        Assert.Empty(references.Check([]));

        Assert.True(references.Require("class-default", target, requireBody: true));
        Assert.True(references.NeedsBody(target));
        Assert.Single(references.Check([]));
        references.Inventory(target);
        Assert.False(references.Require("template", target, requireBody: true));
        Assert.Single(references.Check([]));
        Assert.Empty(references.Check([target.ToLowerInvariant()]));
    }
}
