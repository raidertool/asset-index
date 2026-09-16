using System.Text;
using CUE4Parse.Compression;
using CUE4Parse.FileProvider;
using CUE4Parse.FileProvider.Objects;
using CUE4Parse.UE4.Assets.Objects;
using CUE4Parse.UE4.Readers;
using CUE4Parse.UE4.Versions;
using CUE4Parse.UE4.VirtualFileSystem;

namespace AssetIndex.Tests;

public sealed class LocalizationMergeTests : IDisposable
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), "asset-index-locres-" + Guid.NewGuid().ToString("N"));
    private static readonly AssetText[] Assets = [new(42, new("items", "name", "English source"), null)];

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void IdenticalEntriesSurviveReversedInputOrderAndReadDelays(bool reverse)
    {
        using var provider = Open();
        var a = File("A", "en", "Shared");
        var b = File("B", "en", "Shared");
        a.Delay = reverse ? 20 : 0;
        b.Delay = reverse ? 0 : 20;
        Add(provider, reverse ? [b, a] : [a, b]);
        var issues = new List<ExtractionIssue>();
        var observations = new List<string>();

        var row = Assert.Single(Text.Localize(provider, Assets, issues,
            (_, entries) => observations.Add(entries["items"]["name"])));

        Assert.Equal("Shared", row.DisplayName);
        Assert.Equal(["Shared"], observations);
        Assert.Empty(issues);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ConflictsFailClosedRegardlessOfInputOrderAndReadDelays(bool reverse)
    {
        using var provider = Open();
        var a = File("A", "en", "First");
        var b = File("B", "en", "Second");
        a.Delay = reverse ? 20 : 0;
        b.Delay = reverse ? 0 : 20;
        Add(provider, reverse ? [b, a] : [a, b]);
        var issues = new List<ExtractionIssue>();
        var observed = new List<string>();

        Assert.Empty(Text.Localize(provider, Assets, issues, (locale, _) => observed.Add(locale)));

        var issue = Assert.Single(issues);
        Assert.Equal("localization", issue.Stage);
        Assert.Equal("en", issue.Path);
        Assert.Contains("items:name", issue.Message);
        Assert.Contains(a.Path, issue.Message);
        Assert.Contains(b.Path, issue.Message);
        Assert.Empty(observed);
    }

    [Fact]
    public void IdenticalTextWithDifferentSourceHashesIsAConflict()
    {
        using var provider = Open();
        Add(provider, File("A", "en", "Same text", hash: 1), File("B", "en", "Same text", hash: 2));
        var issues = new List<ExtractionIssue>();

        Assert.Empty(Text.Localize(provider, Assets, issues));
        Assert.Contains("source hash", Assert.Single(issues).Message);
    }

    [Fact]
    public void MountPriorityDoesNotInventPrecedenceBetweenDifferentResourcePaths()
    {
        using var provider = Open();
        Add(provider, File("A", "en", "First", container: "base.pak"),
            File("B", "en", "Second", container: "patch_P.pak"));
        var issues = new List<ExtractionIssue>();

        Assert.Empty(Text.Localize(provider, Assets, issues));
        Assert.Contains("Conflicting localization", Assert.Single(issues).Message);
    }

    [Fact]
    public void HigherMountedPriorityWinsWithoutReadingTheShadowedCopy()
    {
        using var provider = Open();
        var old = File("Game", "en", "Old", container: "base.pak");
        old.Fail = true;
        var current = File("Game", "en", "Current", container: "patch_P.pak");
        Add(provider, old, current);
        var issues = new List<ExtractionIssue>();

        Assert.Equal([current.Path], Localization.Inputs(provider, "en"));
        Assert.Equal("Current", Assert.Single(Text.Localize(provider, Assets, issues)).DisplayName);
        Assert.Equal(0, old.Reads);
        Assert.Equal(1, current.Reads);
        Assert.Empty(issues);
    }

    [Fact]
    public void IdenticalCopiesAtEqualMountPriorityAreHarmless()
    {
        using var provider = Open();
        Add(provider, File("Game", "en", "Same", container: "one.pak"),
            File("Game", "en", "Same", container: "two.pak"));
        var issues = new List<ExtractionIssue>();

        Assert.Equal("Same", Assert.Single(Text.Localize(provider, Assets, issues)).DisplayName);
        Assert.Empty(issues);
    }

    [Theory]
    [InlineData("Different", "name")]
    [InlineData("Same", "different-key")]
    public void DifferentCopiesAtEqualMountPriorityAreNotMerged(string text, string key)
    {
        using var provider = Open();
        var path = File("Game", "en", "Same", container: "one.pak");
        Add(provider, path, new FixtureFile(path.Path, "two.pak", text, key: key));
        var issues = new List<ExtractionIssue>();

        Assert.Empty(Text.Localize(provider, Assets, issues));
        var issue = Assert.Single(issues);
        Assert.Contains("Ambiguous localization mount", issue.Message);
        Assert.Contains("one.pak", issue.Message);
        Assert.Contains("two.pak", issue.Message);
    }

    [Fact]
    public void ReadFailureRejectsItsWholeLocaleWhileOtherLocalesRemainDiagnosticOutput()
    {
        using var provider = Open("en", "ko-KR");
        var broken = File("Broken", "en", "Ignored");
        broken.Fail = true;
        Add(provider, File("Good", "en", "Good English"), broken, File("Game", "ko-KR", "한국어"));
        var issues = new List<ExtractionIssue>();
        var observed = new List<string>();

        Assert.Equal("ko_kr", Assert.Single(Text.Localize(provider, Assets, issues,
            (locale, _) => observed.Add(locale))).Locale);
        Assert.Equal(["ko_kr"], observed);
        Assert.Contains("Broken.locres", Assert.Single(issues).Message);
    }

    [Fact]
    public void EngineFilesDoNotOverrideGameText()
    {
        using var provider = Open();
        Add(provider, File("Game", "en", "Game text"),
            new FixtureFile("Engine/Content/Localization/Game/en/Game.locres", "base.pak", "Engine text"));

        Assert.Equal("Game text", Assert.Single(Text.Localize(provider, Assets, [])).DisplayName);
    }

    [Fact]
    public void ExistingCachedTranslationsAreNotConsumed()
    {
        using var provider = Open();
        Add(provider, File("Game", "en", "From file"));
        provider.Internationalization.Override(new Dictionary<string, IDictionary<string, string>>
        {
            ["items"] = new Dictionary<string, string> { ["name"] = "Cached wrong value" }
        });

        Assert.Equal("From file", Assert.Single(Text.Localize(provider, Assets, [])).DisplayName);
    }

    private DefaultFileProvider Open(params string[] cultures)
    {
        if (cultures.Length == 0) cultures = ["en"];
        var config = Path.Combine(directory, "Config");
        Directory.CreateDirectory(config);
        System.IO.File.WriteAllText(Path.Combine(config, "DefaultGame.ini"), "[/Script/UnrealEd.ProjectPackagingSettings]\n" +
            string.Join("\n", cultures.Select(culture => $"+CulturesToStage={culture}")));
        var provider = new DefaultFileProvider(directory, SearchOption.AllDirectories, pathComparer: StringComparer.OrdinalIgnoreCase);
        provider.Initialize();
        provider.PostMount();
        return provider;
    }

    private static FixtureFile File(string target, string culture, string value, uint hash = 7, string container = "base.pak") =>
        new($"PioneerGame/Content/Localization/{target}/{culture}/{target}.locres", container, value, hash);

    private static void Add(DefaultFileProvider provider, params FixtureFile[] files)
    {
        foreach (var file in files)
            provider.Files.AddFiles(new Dictionary<string, GameFile>(StringComparer.OrdinalIgnoreCase) { [file.Path] = file },
                file.Vfs.ReadOrder);
    }

    public void Dispose() => Directory.Delete(directory, recursive: true);

    private sealed class FixtureFile : VfsEntry
    {
        private readonly byte[] contents;
        public int Delay { get; set; }
        public bool Fail { get; set; }
        public int Reads { get; private set; }
        public override bool IsEncrypted => false;
        public override CompressionMethod CompressionMethod => CompressionMethod.None;

        public FixtureFile(string path, string container, string value, uint hash = 7, string key = "name")
            : base(new FixtureVfs(container), path)
        {
            using var stream = new MemoryStream();
            using var writer = new BinaryWriter(stream);
            foreach (var word in new uint[] { 0x7574140E, 0xFC034A67, 0x9D90154A, 0x1B7F37C3 }) writer.Write(word);
            writer.Write((byte)0); writer.Write(1u); WriteString(writer, "items");
            writer.Write(1u); WriteString(writer, key); writer.Write(hash); WriteString(writer, value);
            contents = stream.ToArray();
        }

        public override byte[] Read(FByteBulkDataHeader? header = null) => contents;
        public override FArchive CreateReader(FByteBulkDataHeader? header = null)
        {
            Reads++;
            if (Fail) throw new IOException("Deliberate read failure");
            Thread.Sleep(Delay);
            return new FByteArchive(Path, contents);
        }

        private static void WriteString(BinaryWriter writer, string value)
        {
            var bytes = Encoding.UTF8.GetBytes(value + "\0");
            writer.Write(bytes.Length); writer.Write(bytes);
        }
    }

    private sealed class FixtureVfs : AbstractVfsReader
    {
        public override string MountPoint { get; protected set; }
        public override bool HasDirectoryIndex => true;

        public FixtureVfs(string name) : base(name, new VersionContainer())
        {
            var mountPoint = "../../../";
            ValidateMountPoint(ref mountPoint);
            MountPoint = mountPoint;
        }

        public override void Mount(StringComparer pathComparer) { }
        public override byte[] Extract(VfsEntry entry, FByteBulkDataHeader? header = null) => throw new NotSupportedException();
        public override void Dispose() { }
    }
}
