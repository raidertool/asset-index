using CUE4Parse.UE4.Objects.UObject;
using static AssetIndex.Tests.ArrayEvidenceFixture;

namespace AssetIndex.Tests;

public sealed class SoftReferenceEvidenceTests
{
    [Theory]
    [InlineData("")]
    [InlineData("None")]
    public void EmptyAssetAndSubobjectPathsAreNull(string assetPath)
    {
        var evidence = Read(new NativeValue(new FSoftObjectPath(assetPath, "")));

        var reference = Assert.Single(evidence.References, item => item.Kind == "soft");
        Assert.True(reference.IsNull);
        Assert.Null(reference.TargetPath);
        Assert.Null(reference.Error);
        Assert.Empty(evidence.Issues);
    }

    [Theory]
    [InlineData("")]
    [InlineData("None")]
    public void ASubobjectWithoutAnAssetPathRemainsAnExplicitFailure(string assetPath)
    {
        var evidence = Read(new NativeValue(new FSoftObjectPath(assetPath, "Child.Leaf")));

        var reference = Assert.Single(evidence.References, item => item.Kind == "soft");
        Assert.False(reference.IsNull);
        Assert.Equal(assetPath + ":Child.Leaf", reference.TargetPath);
        Assert.Equal("Soft reference has a subobject path without an asset path.", reference.Error);
    }

    [Theory]
    [InlineData("", "/Game/A.A")]
    [InlineData("Child.Leaf", "/Game/A.A:Child.Leaf")]
    public void NonemptyAssetPathsKeepTheirExactTarget(string subPath, string target)
    {
        var evidence = Read(new NativeValue(new FSoftObjectPath("/Game/A.A", subPath)));

        var reference = Assert.Single(evidence.References, item => item.Kind == "soft");
        Assert.False(reference.IsNull);
        Assert.Equal(target, reference.TargetPath);
        Assert.Null(reference.Error);
        Assert.Empty(evidence.Issues);
    }
}
