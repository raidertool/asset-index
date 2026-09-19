using System.IO.Compression;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Unicode;

namespace PublishSnapshot.Tests;

public sealed class JsonLinesTests : IDisposable
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), "json-lines-test-" + Guid.NewGuid().ToString("N"));

    public JsonLinesTests() => Directory.CreateDirectory(directory);

    [Theory]
    [InlineData(CompressionLevel.Fastest)]
    [InlineData(CompressionLevel.SmallestSize)]
    public void StandardGzipTrailerPreservesUnicodeAndRowsAcrossManyReads(CompressionLevel level)
    {
        var rows = new[] { "한국어\nFrançais\n日本語", new string('界', 128 * 1024), "last row" };
        var options = new JsonSerializerOptions { Encoder = JavaScriptEncoder.Create(UnicodeRanges.All) };
        var json = string.Join('\n', rows.Select(row => JsonSerializer.Serialize(row, options))) + "\n";

        var actual = Read(Gzip(Encoding.UTF8.GetBytes(json), level));

        Assert.Equal(rows, actual.Select(row => JsonSerializer.Deserialize<string>(row)));
    }

    [Fact]
    public void EmptyGzipHasAValidZeroChecksum() =>
        Assert.Empty(Read(Convert.FromHexString("1f8b08000000000000ff03000000000000000000")));

    [Theory]
    [InlineData(1)]
    [InlineData(4)]
    [InlineData(8)]
    public void CompleteJsonWithoutACompleteGzipTrailerIsRejected(int removedBytes)
    {
        var data = Gzip("{\"value\":1}\n"u8.ToArray());

        Assert.Throws<InvalidDataException>(() => Read(data[..^removedBytes]));
    }

    [Theory]
    [InlineData(8)]
    [InlineData(4)]
    public void IncorrectCrcOrLengthCannotValidateACompleteJsonPrefix(int trailerOffset)
    {
        var data = Gzip("{\"value\":1}\n"u8.ToArray());
        data[^trailerOffset] ^= 1;

        Assert.Throws<InvalidDataException>(() => Read(data));
    }

    [Fact]
    public void InvalidUtf8CannotBecomeAReplacementCharacterInJson() =>
        Assert.Throws<DecoderFallbackException>(() => Read(Gzip([(byte)'"', 0xff, (byte)'"', (byte)'\n'])));

    [Fact]
    public void ConcatenatedNonemptyMembersDoNotMatchASingleStreamTrailer()
    {
        var first = Gzip("{\"value\":1}\n"u8.ToArray());
        var second = Gzip("{\"value\":2}\n"u8.ToArray());

        Assert.Throws<InvalidDataException>(() => Read([.. first, .. second]));
    }

    private List<string> Read(byte[] data)
    {
        var path = Path.Combine(directory, "rows.jsonl.gz");
        File.WriteAllBytes(path, data);
        var rows = new List<string>();
        JsonLines.Read(SnapshotFile.Read(path), row => rows.Add(row.GetRawText()));
        return rows;
    }

    private static byte[] Gzip(byte[] data, CompressionLevel level = CompressionLevel.Fastest)
    {
        using var output = new MemoryStream();
        using (var gzip = new GZipStream(output, level, leaveOpen: true)) gzip.Write(data);
        return output.ToArray();
    }

    public void Dispose() => Directory.Delete(directory, recursive: true);
}
