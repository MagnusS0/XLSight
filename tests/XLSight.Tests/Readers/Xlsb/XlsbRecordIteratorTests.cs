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
}
