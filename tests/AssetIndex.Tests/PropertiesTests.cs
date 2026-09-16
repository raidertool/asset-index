using CUE4Parse.UE4.Assets;
using CUE4Parse.UE4.Assets.Exports;
using CUE4Parse.UE4.Assets.Objects;
using CUE4Parse.UE4.Assets.Objects.Properties;

namespace AssetIndex.Tests;

public class PropertiesTests
{
    [Theory]
    [InlineData("Value", 7)]
    [InlineData("value", 7)]
    [InlineData("VALUE", 9)]
    public void RepeatedDirectFieldsAreAmbiguousEvenWhenTheirValuesMatch(string duplicateName, int duplicateValue)
    {
        var source = new UObject { Name = "Source" };
        source.Properties.Add(Integer("Value", 7));
        source.Properties.Add(Integer(duplicateName, duplicateValue));

        var error = Assert.Throws<InvalidDataException>(() => Properties.Find(source, "value"));

        Assert.Contains("Ambiguous property Source.value", error.Message);
        Assert.Throws<InvalidDataException>(() => Properties.TryGet<int>(source, "Value", out _));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void UniqueFieldsKeepTheirActualNameAndDefiningObject(bool inherited)
    {
        var source = new UObject { Name = "Source" };
        var owner = inherited ? new UObject { Name = "Template" } : source;
        if (inherited) source.Template = new ResolvedLoadedObject(owner);
        var field = Integer("vAlUe", 7);
        owner.Properties.Add(field);
        owner.Properties.Add(Integer("Other", 1));
        owner.Properties.Add(Integer("OTHER", 2));

        var found = Properties.Find(source, "VALUE", out var definedAt);

        Assert.Same(field, found);
        Assert.Equal("vAlUe", found!.Name.Text);
        Assert.Same(owner, definedAt);
    }

    [Fact]
    public void DirectFieldOverridesATemplateFieldWithoutBecomingAmbiguous()
    {
        var template = new UObject { Name = "Template" };
        template.Properties.Add(Integer("Value", 7));
        var source = new UObject { Name = "Source", Template = new ResolvedLoadedObject(template) };
        source.Properties.Add(Integer("value", 0));

        Assert.True(Properties.TryGet<int>(source, "VALUE", out var value, out var definedAt));
        Assert.Equal(0, value);
        Assert.Same(source, definedAt);
    }

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

    [Fact]
    public void SubobjectLookupNeverDecodesAnUncreatedSiblingOuter()
    {
        var package = new CrawlerPackage("Plugin/Parents.uasset", "/Plugin/Parents",
            new CrawlerExport("Wrong", "Object") { OnLoad = _ => throw new InvalidOperationException("Sibling outer was decoded.") },
            new CrawlerExport("Right", "Object"),
            new CrawlerExport("Child", "Object") { OuterIndex = 0 },
            new CrawlerExport("Child", "Object") { OuterIndex = 1 },
            new CrawlerExport("Leaf", "Object") { OuterIndex = 2 },
            new CrawlerExport("Leaf", "Object") { OuterIndex = 3 });
        var parent = package.ExportsLazy[1].Value;

        var leaf = Properties.ResolveSubobject(parent, "child.LEAF");

        Assert.Same(package.ExportsLazy[5].Value, leaf);
        Assert.Equal([1, 3, 5], package.BodyReads);
    }

    [Fact]
    public void LoadedOutersWithTheSamePathRemainDifferentIdentities()
    {
        var package = new CrawlerPackage("Plugin/Parents.uasset", "/Plugin/Parents",
            new CrawlerExport("Parent", "Object"), new CrawlerExport("Parent", "Object"),
            new CrawlerExport("Child", "Object") { OuterIndex = 0 },
            new CrawlerExport("Child", "Object") { OuterIndex = 1 });
        var wrong = package.ExportsLazy[0].Value;
        var right = package.ExportsLazy[1].Value;
        Assert.Equal(ObjectMetadata.Path(wrong), ObjectMetadata.Path(right));

        var child = Properties.ResolveSubobject(right, "Child");

        Assert.Same(package.ExportsLazy[3].Value, child);
        Assert.Equal([0, 1, 3], package.BodyReads);
    }

    private static FPropertyTag Integer(string name, int value) =>
        new("IntProperty", new IntProperty(value)) { Name = name };
}
