using System.Buffers.Binary;
using System.IO.Compression;
using System.IO.Hashing;
using System.Text;
using System.Text.Json;

namespace PublishSnapshot;

internal static class JsonLines
{
    public static void Read(SnapshotFile file, Action<JsonElement> inspect)
    {
        using var input = File.OpenRead(file.Path);
        Preview.Require(input.Length >= 18, "Truncated gzip stream.");
        input.Position = input.Length - 8;
        Span<byte> trailer = stackalloc byte[8];
        input.ReadExactly(trailer);
        input.Position = 0;
        using var gzip = new GZipStream(input, CompressionMode.Decompress);
        using var measured = new MeasuredStream(gzip);
        using var reader = new StreamReader(measured, new UTF8Encoding(false, true));
        while (reader.ReadLine() is { } line)
        {
            using var row = JsonDocument.Parse(line);
            inspect(row.RootElement);
        }
        // Some decompressors accept EOF before the gzip footer. Verify its checksum
        // and length explicitly so a complete-looking JSON prefix cannot pass.
        Preview.Require(BinaryPrimitives.ReadUInt32LittleEndian(trailer) == measured.Checksum &&
            BinaryPrimitives.ReadUInt32LittleEndian(trailer[4..]) == unchecked((uint)measured.BytesRead), "Gzip checksum or length mismatch.");
    }

    private sealed class MeasuredStream(Stream source) : Stream
    {
        private readonly Crc32 crc = new();
        public uint Checksum => crc.GetCurrentHashAsUInt32();
        public long BytesRead { get; private set; }
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override int Read(byte[] buffer, int offset, int count) => Read(buffer.AsSpan(offset, count));
        public override int Read(Span<byte> buffer)
        {
            var count = source.Read(buffer);
            crc.Append(buffer[..count]);
            BytesRead += count;
            return count;
        }
        public override void Flush() => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
