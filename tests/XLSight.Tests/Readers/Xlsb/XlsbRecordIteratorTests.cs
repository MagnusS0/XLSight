using XLSight.Internal.Readers.Xlsb;
using Xunit;

namespace XLSight.Tests.Readers.Xlsb;

public sealed class XlsbRecordIteratorTests
{
    [Fact]
    public void TryRead_DecodesVariableLengthRecordTypeAndPayloadLength()
    {
        byte[] payload = new byte[130];
        payload.AsSpan().Fill(0x5A);

        using var stream = new MemoryStream();
        XlsbTestRecords.WriteRecord(stream, XlsbRecordType.BrtEndSheetData, payload);
        stream.Position = 0;

        using var iterator = new XlsbRecordIterator(stream);

        Assert.True(iterator.TryRead(out XlsbRecord record));
        Assert.Equal(XlsbRecordType.BrtEndSheetData, record.Type);
        Assert.Equal(payload.Length, record.Payload.Length);
        Assert.True(record.Payload.SequenceEqual(payload));
        Assert.False(iterator.TryRead(out _));
    }

    [Fact]
    public void TryRead_TruncatedPayload_ThrowsMalformedWorkbookException()
    {
        using var stream = new MemoryStream([XlsbRecordType.BrtCellBool, 2, 1]);
        using var iterator = new XlsbRecordIterator(stream);

        Assert.Throws<MalformedWorkbookException>(() => iterator.TryRead(out _));
    }

    [Fact]
    public void TryRead_OversizedPayloadLength_ThrowsMalformedWorkbookException()
    {
        using var stream = new MemoryStream();
        XlsbTestRecords.WriteVarInt(stream, XlsbRecordType.BrtCellReal);
        XlsbTestRecords.WriteVarInt(stream, XlsbRecordIterator.MaxRecordPayloadLength + 1);
        stream.Position = 0;

        using var iterator = new XlsbRecordIterator(stream);

        Assert.Throws<MalformedWorkbookException>(() => iterator.TryRead(out _));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(7)]
    public void TryRead_HandlesShortReadsAcrossRecordBoundaries(int chunkSize)
    {
        byte[] firstPayload = new byte[130];
        firstPayload.AsSpan().Fill(0x5A);
        byte[] secondPayload = new byte[70_000];
        secondPayload.AsSpan().Fill(0xA5);

        using var data = new MemoryStream();
        XlsbTestRecords.WriteRecord(data, XlsbRecordType.BrtEndSheetData, firstPayload);
        XlsbTestRecords.WriteRecord(data, XlsbRecordType.BrtCellReal, secondPayload);
        data.Position = 0;

        using var stream = new ChunkedReadStream(data, chunkSize);
        using var iterator = new XlsbRecordIterator(stream);

        Assert.True(iterator.TryRead(out XlsbRecord first));
        Assert.Equal(XlsbRecordType.BrtEndSheetData, first.Type);
        Assert.True(first.Payload.SequenceEqual(firstPayload));

        Assert.True(iterator.TryRead(out XlsbRecord second));
        Assert.Equal(XlsbRecordType.BrtCellReal, second.Type);
        Assert.True(second.Payload.SequenceEqual(secondPayload));
        Assert.False(iterator.TryRead(out _));
    }

    private sealed class ChunkedReadStream(Stream inner, int maxChunkSize) : Stream
    {
        public override bool CanRead => inner.CanRead;
        public override bool CanSeek => inner.CanSeek;
        public override bool CanWrite => false;
        public override long Length => inner.Length;

        public override long Position
        {
            get => inner.Position;
            set => inner.Position = value;
        }

        public override int Read(byte[] buffer, int offset, int count) =>
            inner.Read(buffer, offset, Math.Min(count, maxChunkSize));

        public override int Read(Span<byte> buffer) =>
            inner.Read(buffer[..Math.Min(buffer.Length, maxChunkSize)]);

        public override void Flush() => inner.Flush();
        public override long Seek(long offset, SeekOrigin origin) => inner.Seek(offset, origin);
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
