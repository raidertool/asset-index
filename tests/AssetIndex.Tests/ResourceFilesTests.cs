namespace AssetIndex.Tests;

public sealed class ResourceFilesTests
{
    [Theory]
    [InlineData("/Game/Pioneer/UI/T_Icon.T_Icon", "images/Game/Pioneer/UI/T_Icon.png")]
    [InlineData("/Plugin/Icons/Bundle.Export", "images/Plugin/Icons/Export.png")]
    [InlineData("/Game/UI/T_InroundCraft.T_InRoundCraft", "images/Game/UI/T_InRoundCraft.png")]
    [InlineData("/Game/Nested.Parent:Icon", "images/Game/Parent/Icon.png")]
    [InlineData("/Game/Nested.Parent:Child.Icon", "images/Game/Parent/Child/Icon.png")]
    [InlineData("Fixture", "images/Fixture.png")]
    [InlineData("/Game/日本語.Icon", "images/Game/Icon.png")]
    public void PreservesOriginalDirectoriesAndObjectBasenames(string path, string expected) =>
        Assert.Equal(expected, ResourceFiles.ImagePath(path));

    [Theory]
    [InlineData("")]
    [InlineData("/../Escape.Icon")]
    [InlineData("//Game/Icon.Icon")]
    [InlineData("/Game/.git/Icon.Icon")]
    [InlineData("/Game/Icon..")]
    [InlineData("/Game/Icon.Icon:")]
    [InlineData("/Game/Bad?Package.Icon")]
    [InlineData("/Game/Icon.Bad\\Name")]
    [InlineData("/Game/Icon.Icon\n")]
    [InlineData("/Game/Icon.Icon ")]
    [InlineData("/Game/Icon.NUL")]
    [InlineData("/Game/COM1/Icon.Icon")]
    public void RejectsUnsafeNamesInsteadOfRenamingThem(string path) =>
        Assert.Throws<InvalidDataException>(() => ResourceFiles.ImagePath(path));

    [Theory]
    [InlineData("images/Game/UI/Icon.png", true)]
    [InlineData("images/Game/日本語.png", true)]
    [InlineData("images/Game/../Icon.png", false)]
    [InlineData("images/.git/Icon.png", false)]
    [InlineData("images/Game/CON.png", false)]
    [InlineData("images/Game/.png", false)]
    [InlineData("images/Game/Icon.PNG", false)]
    [InlineData("images/Game/Icon\t.png", false)]
    [InlineData("images/Game\\Icon.png", false)]
    [InlineData("/images/Game/Icon.png", false)]
    public void ValidatesPublishedImagePathsUsingTheSamePortableRules(string path, bool expected) =>
        Assert.Equal(expected, ResourceFiles.IsImagePath(path));

    [Theory]
    [InlineData("%20")]
    [InlineData("#detail")]
    public void RejectsUrlEscapesAndFragmentsInSourceAndPublishedNames(string suffix)
    {
        Assert.Throws<InvalidDataException>(() => ResourceFiles.ImagePath($"/Game/Icon.Icon{suffix}"));
        Assert.False(ResourceFiles.IsImagePath($"images/Game/Icon{suffix}.png"));
    }
}
