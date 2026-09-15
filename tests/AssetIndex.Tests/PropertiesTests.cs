using CUE4Parse.UE4.Assets;
using CUE4Parse.UE4.Assets.Exports;

namespace AssetIndex.Tests;

public class PropertiesTests
{
    [Fact]
    public void SoftSubobjectsFollowTheDeclaredOuterAtEveryLevel()
    {
        var first = new UObject { Name = "First" };
        var second = new UObject { Name = "Second" };
        var wrongChild = new UObject { Name = "Child" };
        var child = new UObject { Name = "Child" };
        var wrongLeaf = new UObject { Name = "Leaf" };
        var leaf = new UObject { Name = "Leaf" };
        UObject[] exports = [first, second, wrongChild, child, wrongLeaf, leaf];
        var package = new FixturePackage(exports);
        first.Outer = new ResolvedPackageObject(package);
        second.Outer = new ResolvedPackageObject(package);
        wrongChild.Outer = new ResolvedLoadedObject(first);
        child.Outer = new ResolvedLoadedObject(second);
        wrongLeaf.Outer = new ResolvedLoadedObject(wrongChild);
        leaf.Outer = new ResolvedLoadedObject(child);

        Assert.Same(leaf, Properties.ResolveSubobject(second, "child.LEAF"));
        Assert.Throws<InvalidDataException>(() => Properties.ResolveSubobject(second, "Leaf"));
    }

    [Fact]
    public void DuplicateChildrenAreAnErrorInsteadOfFirstExportWins()
    {
        var parent = new UObject { Name = "Parent" };
        var first = new UObject { Name = "Child" };
        var second = new UObject { Name = "Child" };
        parent.Outer = new ResolvedPackageObject(new FixturePackage(parent, first, second));
        first.Outer = new ResolvedLoadedObject(parent);
        second.Outer = new ResolvedLoadedObject(parent);

        Assert.Contains("Ambiguous", Assert.Throws<InvalidDataException>(() => Properties.ResolveSubobject(parent, "Child")).Message);
    }
}
