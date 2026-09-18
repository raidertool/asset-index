using System.Text;
using CUE4Parse.FileProvider;

namespace AssetIndex.Tests;

public sealed class LocalizationTests : IDisposable
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), "asset-index-localization-" + Guid.NewGuid().ToString("N"));
    private static readonly AssetText[] Assets = [new(42, new("items", "name", "English source"), null)];

    [Theory]
    [InlineData("pt-BR", "pt_br")]
    [InlineData("zh-Hans", "zh_hans")]
    [InlineData("zh-Hant", "zh_hant")]
    [InlineData("fr-CA", "fr_ca")]
    [InlineData("ko-KR", "ko_kr")]
    [InlineData("EN", "en")]
    public void LoadsTheActualAvailableCultureAndNormalizesOnlyTheOutput(string culture, string locale)
    {
        WriteTranslation(culture, "Translated name");
        using var provider = OpenProvider([culture]);
        var issues = new List<ExtractionIssue>();

        var row = Assert.Single(Text.Localize(provider, Assets, issues));

        Assert.Equal(culture, Assert.Single(provider.Internationalization.AvailableCultures));
        Assert.Null(provider.Internationalization.Culture);
        Assert.Equal(new LocalizedText(42, locale, "Translated name", ""), row);
        Assert.Empty(issues);
    }

    [Fact]
    public void DoesNotInventCulturesFromOtherLocalizationFiles()
    {
        WriteTranslation("en", "English name");
        WriteTranslation("fr", "French name");
        using var provider = OpenProvider(["en"]);
        var issues = new List<ExtractionIssue>();

        var row = Assert.Single(Text.Localize(provider, Assets, issues));

        Assert.Equal("en", row.Locale);
        Assert.Empty(issues);
    }

    [Fact]
    public void RemappedCultureIsReportedInsteadOfMislabelingItsTranslation()
    {
        WriteTranslation("fr", "French name");
        using var provider = OpenProvider(["en", "fr"], "[Internationalization]\n+CultureMappings=\"en;fr\"");
        var issues = new List<ExtractionIssue>();

        var row = Assert.Single(Text.Localize(provider, Assets, issues));

        Assert.Equal("fr", row.Locale);
        Assert.Equal("en", Assert.Single(issues).Path);
        Assert.Contains("Culture en resolved to fr", issues[0].Message);
    }

    [Fact]
    public void NoAvailableCulturesIsReported()
    {
        using var provider = OpenProvider([]);
        var issues = new List<ExtractionIssue>();

        Assert.Empty(Text.Localize(provider, Assets, issues));
        Assert.Equal("cultures", Assert.Single(issues).Path);
    }

    [Fact]
    public void AvailableCultureWithoutEntriesIsReported()
    {
        using var provider = OpenProvider(["fr"]);
        var issues = new List<ExtractionIssue>();

        Assert.Empty(Text.Localize(provider, Assets, issues));
        Assert.Contains("No localization entries loaded for fr", Assert.Single(issues).Message);
    }

    [Theory]
    [InlineData("fr", "fr")]
    [InlineData("ko-KR", "ko_kr")]
    public void InvariantNamesSurviveEveryCultureWithoutFillingMissingTranslations(string culture, string locale)
    {
        WriteTranslation("en", "An unrelated name");
        WriteTranslation(culture, "An unrelated name");
        using var provider = OpenProvider(["en", culture]);
        AssetText[] assets =
        [
            new(1, new("", "", "XP", CultureInvariant: true), null),
            new(2, new("items", "missing", "English source"), null)
        ];
        var issues = new List<ExtractionIssue>();

        var rows = Text.Localize(provider, assets, issues);

        Assert.Equal(4, rows.Count);
        Assert.Contains(new LocalizedText(1, "en", "XP", ""), rows);
        Assert.Contains(new LocalizedText(1, locale, "XP", ""), rows);
        Assert.Contains(new LocalizedText(2, "en", "", ""), rows);
        Assert.Contains(new LocalizedText(2, locale, "", ""), rows);
        Assert.Empty(issues);
    }

    [Fact]
    public void ObserverSeesEveryValidCultureEvenWithoutCatalogAssets()
    {
        WriteTranslation("en", "Unowned English string");
        WriteTranslation("fr", "Unowned French string");
        using var provider = OpenProvider(["en", "fr", "de"]);
        var observed = new Dictionary<string, string>();
        var issues = new List<ExtractionIssue>();

        Assert.Empty(Text.Localize(provider, [], issues,
            (locale, entries) => observed.Add(locale, entries["items"]["name"])));

        Assert.Equal("Unowned English string", observed["en"]);
        Assert.Equal("Unowned French string", observed["fr"]);
        Assert.Equal(2, observed.Count);
        Assert.Equal("de", Assert.Single(issues).Path);
    }

    [Fact]
    public void ObserverWriteFailureIsNotSilentlySkipped()
    {
        WriteTranslation("en", "English name");
        using var provider = OpenProvider(["en"]);
        Assert.Throws<IOException>(() => Text.Localize(provider, [], [], (_, _) => throw new IOException("Output failed")));
    }

    private DefaultFileProvider OpenProvider(string[] cultures, string extraConfig = "")
    {
        var config = Path.Combine(directory, "Config");
        Directory.CreateDirectory(config);
        File.WriteAllText(Path.Combine(config, "DefaultGame.ini"),
            "[/Script/UnrealEd.ProjectPackagingSettings]\n" +
            string.Join("\n", cultures.Select(culture => $"+CulturesToStage={culture}")) + "\n" + extraConfig);
        var provider = new DefaultFileProvider(directory, SearchOption.AllDirectories, pathComparer: StringComparer.OrdinalIgnoreCase);
        provider.Initialize();
        provider.PostMount();
        return provider;
    }

    private void WriteTranslation(string culture, string value)
    {
        var path = Path.Combine(directory, "Content", "Localization", "Game", culture);
        Directory.CreateDirectory(path);
        using var writer = new BinaryWriter(File.Create(Path.Combine(path, "Game.locres")));
        // Locres magic plus version 0 stores one namespace/key/string directly.
        foreach (var word in new uint[] { 0x7574140E, 0xFC034A67, 0x9D90154A, 0x1B7F37C3 }) writer.Write(word);
        writer.Write((byte)0);
        writer.Write(1u);
        WriteString(writer, "items");
        writer.Write(1u);
        WriteString(writer, "name");
        writer.Write(0u);
        WriteString(writer, value);
    }

    private static void WriteString(BinaryWriter writer, string value)
    {
        var bytes = Encoding.UTF8.GetBytes(value + "\0");
        writer.Write(bytes.Length);
        writer.Write(bytes);
    }

    public void Dispose() => Directory.Delete(directory, recursive: true);
}
