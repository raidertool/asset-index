using System.Text.Json;
using System.Text.Json.Nodes;

namespace PublishSnapshot.Tests;

public sealed class LocalizedTextChecksTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AllResolvableCulturesAreRequiredIncludingInvariantText(bool invariant)
    {
        var name = Reference("Name", invariant);
        var dictionaries = new[] { Dictionary("en", "Translated"), Dictionary("ko_kr", "한국어") };
        var english = invariant ? "Name" : "Translated";
        var korean = invariant ? "Name" : "한국어";
        Validate(name, null, dictionaries, Row("en", english), Row("ko_kr", korean));

        Assert.Throws<InvalidDataException>(() => Validate(name, null, dictionaries, Row("en", english)));
        Assert.Throws<InvalidDataException>(() => Validate(name, null, dictionaries, Row("en", english), Row("ko_kr", "wrong")));
    }

    [Fact]
    public void MissingAndEmptyTranslationsStayEmptyInEveryLocale()
    {
        var name = Reference("Source", false);
        var dictionaries = new[] { Dictionary("en"), Dictionary("ko_kr") };
        Validate(name, null, dictionaries, Row("en", ""), Row("ko_kr", ""));
        Assert.Throws<InvalidDataException>(() => Validate(name, null, dictionaries, Row("en", "Source"), Row("ko_kr", "")));
        dictionaries[0] = Dictionary("en", "");
        Validate(name, null, dictionaries, Row("en", ""), Row("ko_kr", ""));
    }

    [Fact]
    public void DescriptionOnlyAndEmptyRowsAreRequired()
    {
        var description = Reference("Description", true);
        var dictionaries = new[] { Dictionary("en") };
        Assert.Equal((false, true), Validate(null, description, dictionaries, Row("en", "", "Description")));
        Assert.Throws<InvalidDataException>(() => Validate(null, description, dictionaries, Row("en", "", "Wrong")));
        Validate(null, null, dictionaries, Row("en", ""));
        Assert.Throws<InvalidDataException>(() => Validate(null, null, dictionaries));
    }

    [Fact]
    public void ExtraUnknownDuplicateAndNoncanonicalLocalesFail()
    {
        var name = Reference("Name", true);
        var dictionaries = new[] { Dictionary("en") };
        foreach (var locale in new[] { "en", "EN", "ko_kr" })
            Assert.Throws<InvalidDataException>(() => Validate(name, null, dictionaries, Row("en", "Name"), Row(locale, "Name")));
    }

    private static LocalizationEvidence Dictionary(string locale, string? value = null)
    {
        var entries = new Dictionary<(string, string), string>();
        if (value is not null) entries.Add(("Items", "Key"), value);
        return new(locale, entries);
    }

    private static JsonObject Reference(string source, bool invariant) => new()
    {
        ["namespace"] = "Items",
        ["key"] = "Key",
        ["source"] = source,
        ["cultureInvariant"] = invariant
    };

    private static JsonObject Row(string locale, string name, string description = "") => new()
    {
        ["locale"] = locale,
        ["displayName"] = name,
        ["description"] = description
    };

    private static (bool Name, bool Description) Validate(JsonNode? name, JsonNode? description,
        IReadOnlyList<LocalizationEvidence> dictionaries, params JsonNode[] rows)
    {
        var presentation = new JsonObject { ["name"] = name?.DeepClone(), ["description"] = description?.DeepClone() };
        using var source = JsonDocument.Parse(presentation.ToJsonString());
        using var text = JsonDocument.Parse(new JsonArray(rows).ToJsonString());
        return LocalizedTextChecks.Validate(text.RootElement, dictionaries, source.RootElement);
    }
}

public sealed partial class PublisherTests
{
    [Theory]
    [InlineData("name")]
    [InlineData("description")]
    [InlineData("missing-korean")]
    public void IncorrectLocalizedOutputFailsBeforeRemoteAccess(string failure)
    {
        if (failure == "missing-korean")
            WriteLines(preview, "localization/ko_kr.jsonl.gz", new[] { new { @namespace = "UI", key = "Other", value = "한국어" } });
        else
            ChangeJson("assets.json", rows => rows[0]!["text"]![0]![failure == "name" ? "displayName" : "description"] = "Unrelated string");
        var before = remoteGit.Run("show-ref");

        var error = Assert.Throws<InvalidDataException>(() => Publisher.Publish(preview,
            Path.Combine(root, "nonexistent-remote"), NextExtractor, "456"));

        Assert.Contains(failure == "missing-korean" ? "missing translation" : "Rendered text", error.Message);
        Assert.Equal(before, remoteGit.Run("show-ref"));
    }
}
