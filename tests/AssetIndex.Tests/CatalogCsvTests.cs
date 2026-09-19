using System.Text;
using System.Text.Json;

namespace AssetIndex.Tests;

public sealed class CatalogCsvTests
{
    [Fact]
    public void TechnicalNameUsesActualDefinitionBeforePersistenceAndIsOrderIndependent()
    {
        var definitions = new ObjectReference[]
        {
            new("Persistence", "PersistenceDataAsset", "/Game/A.A"),
            new("Second", "ItemDataAsset", "/Game/Z.Z"),
            new("Actual", "ItemDataAsset", "/Game/B.B")
        };
        var asset = Asset() with { Definitions = definitions };
        var original = Row(asset);
        Assert.Equal("Actual", original[1]);
        Assert.Equal(original, Row(asset with { Definitions = definitions.Reverse().ToArray() }));
        Assert.Equal("Persistence", Row(asset with { Definitions = [definitions[0]] })[1]);
        Assert.Throws<InvalidDataException>(() => Row(asset with { Definitions = [] }));
    }

    [Fact]
    public void EnglishConvenienceFallbackDoesNotBecomeATranslationOrChangeAuthoredSource()
    {
        var asset = Asset() with
        {
            Text = [new("en", "", "English translation"), new("de", "", ""), new("ja", "日本語", "")],
            Presentation = new(new("items", "name", "Authored name"), new("items", "description", "Authored description"), [], [])
        };
        Assert.Equal(["42", "Actual", "Authored name", "English translation", "", ""], Row(asset));
        var localized = CatalogCsv.LocalizationRows([asset]).ToArray();
        Assert.Equal(["42", "de", "", ""], localized[1]);
        Assert.Equal(["42", "en", "", "English translation"], localized[2]);
        Assert.Equal(["42", "ja", "日本語", ""], localized[3]);
        Assert.Equal("Authored name", asset.Presentation.Name!.Source);
    }

    [Fact]
    public void LocalizationsIncludeEveryAssetForEachAvailableLocaleWithoutFillingMissingCells()
    {
        var populated = Asset() with { Id = -5, Text = [new("ko_kr", "이름", "설명"), new("en", "Name", "")] };
        var blank = Asset();
        var rows = CatalogCsv.LocalizationRows([blank, populated]).ToArray();
        Assert.Equal(5, rows.Length);
        Assert.Equal(["-5", "en", "Name", ""], rows[1]);
        Assert.Equal(["42", "en", "", ""], rows[3]);
        Assert.Equal(["42", "ko_kr", "", ""], rows[4]);
    }

    [Fact]
    public void BothCsvFilesHaveStableNumericIdAndLocaleOrderRegardlessOfInputArrival()
    {
        var assets = new long[] { 10, -2, 2, -10 }.Select(id => Asset() with
        {
            Id = id,
            Text = [new("ja", "日本語", ""), new("en", "English", "")]
        }).ToArray();
        var reversed = assets.Reverse()
            .Select(asset => asset with { Text = asset.Text.Reverse().ToArray() }).ToArray();
        var expectedMain = Encoding.UTF8.GetBytes(
            "asset_id,asset_name,display_name,description,image,wide_image\n" +
            "-10,Actual,English,,,\n-2,Actual,English,,,\n2,Actual,English,,,\n10,Actual,English,,,\n");
        var expectedLocalizations = Encoding.UTF8.GetBytes(
            "asset_id,locale,display_name,description\n" +
            "-10,en,English,\n-10,ja,日本語,\n-2,en,English,\n-2,ja,日本語,\n" +
            "2,en,English,\n2,ja,日本語,\n10,en,English,\n10,ja,日本語,\n");

        foreach (var input in new[] { assets, reversed })
        {
            Assert.Equal(expectedMain, CsvBytes(CatalogCsv.MainRows(input)));
            Assert.Equal(expectedLocalizations, CsvBytes(CatalogCsv.LocalizationRows(input)));
        }
    }

    [Fact]
    public void DefaultUsesIconAndNpcSpecificOwnerWinsEqualFieldTies()
    {
        var generic = new ObjectReference("Generic", "UIItemMetaDataItem", "/Game/A.A");
        var npc = new ObjectReference("Npc", "UINPCMetaDataItem", "/Game/Z.Z");
        var images = new[] { Image("Icon", generic.Path, "generic.png"), Image("Icon", npc.Path, "npc.png"),
            Image("PreviewImage", npc.Path, "portrait.png") };
        var asset = Asset() with { Definitions = [new("Actual", "NPCItemDataAsset", "/Game/Actual.Actual")],
            Metadata = [generic, npc], Images = images };
        Assert.Equal("npc.png", Row(asset)[4]);
        Assert.Equal(Row(asset), Row(asset with { Images = images.Reverse().ToArray() }));
        Assert.Equal("generic.png", Row(asset with { Definitions = Asset().Definitions })[4]);
    }

    [Theory]
    [InlineData(512, 512, "")]
    [InlineData(512, 1024, "")]
    [InlineData(1024, 512, "big.png")]
    public void WideRequiresLandscapePixelsEvenForBigIcon(int width, int height, string expected)
    {
        var asset = Asset() with { Images = [Image("Icon", "Owner", "icon.png"),
            Image("BigIcon", "Owner", "big.png") with { Width = width, Height = height }] };
        Assert.Equal("icon.png", Row(asset)[4]);
        Assert.Equal(expected, Row(asset)[5]);
    }

    [Fact]
    public void WidePrefersExplicitAspectRoleAndNeverBorrowsMapRegionOrControlImages()
    {
        var asset = Asset() with { Images = [Image("BigIcon", "Owner", "big.png", 1024, 512),
            Image("OfferImage_16x9", "Owner", "offer.png", 1920, 1080),
            Image("MapAreas[0].HeaderImage", "Owner", "region.png", 2000, 1000),
            Image("UnlockVideoPreviewImage", "Owner", "video.png", 2000, 1000)] };
        Assert.Equal("offer.png", Row(asset)[5]);
        Assert.Equal(["", ""], Row(asset with { Images = asset.Images.Skip(2).ToArray() })[4..]);
    }

    [Fact]
    public void FailedOrUnknownRolesDoNotBecomeDefaults()
    {
        var asset = Asset() with { Images = [Image("Icon", "Owner", "bad.png") with { Status = "failed" },
            Image("ImaginaryIcon", "Owner", "unknown.png"), Image("image", "Owner", "good.png")] };
        Assert.Equal("good.png", Row(asset)[4]);
        Assert.Equal("", Row(asset)[5]);
    }

    [Fact]
    public void CsvIsUtf8WithoutBomUsesLfAndQuotesAuthoredPunctuationWithoutChangingText()
    {
        using var stream = new MemoryStream();
        CatalogCsv.Write(stream, [["asset_id", "description"], ["-1", "日本語,\"quoted\"\r\nnext"]]);
        Assert.Equal("asset_id,description\n-1,\"日本語,\"\"quoted\"\"\r\nnext\"\n", Encoding.UTF8.GetString(stream.ToArray()));
    }

    [Fact]
    public void SnapshotRecordsRoundTripForValidatedReplayWithoutReinterpretingIds()
    {
        var original = Asset() with { Id = -42 };
        var json = JsonSerializer.Serialize(original, Snapshot.Json);
        Assert.Contains("\"id\": \"-42\"", json);
        Assert.Equal(Row(original), Row(JsonSerializer.Deserialize<AssetRecord>(json, Snapshot.Json)!));
    }

    private static AssetRecord Asset() => new(42, [new("Actual", "ItemDataAsset", "/Game/Actual.Actual")], [], [], []);
    private static AssetImage Image(string field, string source, string file, int width = 512, int height = 512) =>
        new(field, source, "/Game/Texture.Texture", "exported", file, width, height);
    private static string[] Row(AssetRecord asset) => CatalogCsv.MainRows([asset]).Skip(1).Single();
    private static byte[] CsvBytes(IEnumerable<string[]> rows)
    {
        using var stream = new MemoryStream();
        CatalogCsv.Write(stream, rows);
        return stream.ToArray();
    }
}
