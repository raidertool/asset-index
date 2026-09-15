using CUE4Parse.UE4.Assets;
using CUE4Parse.UE4.Assets.Exports;
using CUE4Parse.UE4.Assets.Objects;
using CUE4Parse.UE4.Assets.Objects.Properties;
using CUE4Parse.UE4.Objects.Core.i18N;
using CUE4Parse.UE4.Objects.UObject;

namespace AssetIndex.Tests;

public sealed class TextTests
{
    [Fact]
    public void ReadsNamespaceKeyAndSourceInsteadOfThePreviouslyLoadedTranslation()
    {
        var metadata = WithText("ItemName", new FText("items", "name", "Source name", "Cached French"));
        var issues = new List<ExtractionIssue>();

        var text = Text.Read(Asset(metadata), issues);

        Assert.Equal(new TextReference("items", "name", "Source name"), text.Name);
        Assert.Empty(issues);
    }

    [Fact]
    public void ReadsInheritedNameAndDirectDescription()
    {
        var parent = WithText("ItemName", new FText("items", "name", "Parent name"));
        var child = WithText("Description", new FText("items", "description", "Child description"));
        child.Template = new ResolvedLoadedObject(parent);

        var text = Text.Read(Asset(child), []);

        Assert.Equal("Parent name", text.Name?.Source);
        Assert.Equal("Child description", text.Description?.Source);
    }

    [Fact]
    public void DirectNameOverridesInheritedName()
    {
        var child = WithText("ItemName", new FText("items", "child", "Child name"));
        child.Template = new ResolvedLoadedObject(WithText("ItemName", new FText("Parent name")));

        Assert.Equal("Child name", Text.Read(Asset(child), []).Name?.Source);
    }

    [Fact]
    public void ExplicitlyEmptyNameDoesNotRestoreTheParentName()
    {
        var child = WithText("ItemName", new FText(string.Empty));
        child.Template = new ResolvedLoadedObject(WithText("ItemName", new FText("Parent name")));

        Assert.Null(Text.Read(Asset(child), []).Name);
    }

    [Fact]
    public void CurrencyLongNameIsNotExportedAsADescription()
    {
        var metadata = WithText("LongName", new FText("Experience points"));

        var text = Text.Read(Asset(metadata), []);

        Assert.Equal("Experience points", text.Name?.Source);
        Assert.Null(text.Description);
    }

    [Fact]
    public void MissingTranslationUsesSourceOnlyForEnglish()
    {
        var reference = new TextReference("items", "name", "Source name");
        var translations = new Dictionary<string, IReadOnlyDictionary<string, string>>();

        Assert.Equal("Source name", Text.Resolve(reference, translations, "en"));
        Assert.Equal(string.Empty, Text.Resolve(reference, translations, "fr"));
    }

    [Fact]
    public void ExplicitTranslationEqualToEnglishStillCountsAsTranslated()
    {
        var reference = new TextReference("items", "name", "XP");
        var translations = new Dictionary<string, IReadOnlyDictionary<string, string>>
        {
            ["items"] = new Dictionary<string, string> { ["name"] = "XP" }
        };

        Assert.Equal("XP", Text.Resolve(reference, translations, "fr"));
    }

    [Fact]
    public void MissingKeyDoesNotUseAnUnrelatedNamespaceOrEmptyTranslationFallback()
    {
        var translations = new Dictionary<string, IReadOnlyDictionary<string, string>>
        {
            ["other"] = new Dictionary<string, string> { ["name"] = "Wrong namespace" },
            ["items"] = new Dictionary<string, string> { ["blank"] = string.Empty }
        };

        Assert.Equal(string.Empty, Text.Resolve(new("items", "name", "Source"), translations, "de"));
        Assert.Equal(string.Empty, Text.Resolve(new("items", "blank", "Source"), translations, "en"));
    }

    [Fact]
    public void UnsupportedFormattedHistoryIsReportedWithoutPublishingTheTemplate()
    {
        var metadata = WithText("ItemName", new FText(0, ETextHistoryType.NamedFormat, new FormattedHistory()));
        var issues = new List<ExtractionIssue>();

        var text = Text.Read(Asset(metadata), issues);

        Assert.Null(text.Name);
        Assert.Contains("NamedFormat", Assert.Single(issues).Message);
    }

    private static CatalogAsset Asset(UObject metadata) =>
        new(1, new UObject { Name = "DA_Test" }, [metadata]);

    private static UObject WithText(string field, FText text)
    {
        var value = new UObject { Name = "UI_Test" };
        value.Properties.Add(new FPropertyTag(new FName("TextProperty"), new TextProperty(text))
        {
            Name = new FName(field)
        });
        return value;
    }

    private sealed class FormattedHistory : FTextHistory
    {
        public override string Text => "Unlock {Item}";
    }
}
