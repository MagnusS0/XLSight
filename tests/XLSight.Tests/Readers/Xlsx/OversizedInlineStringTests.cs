using System.Text;
using XLSight.Internal.Metadata;
using XLSight.Internal.Readers.Xlsx;
using Xunit;

namespace XLSight.Tests.Readers.Xlsx;

public sealed class OversizedInlineStringTests
{
    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task MaxLengthCjkText_SurvivesBufferGrowth_AndPreservesFollowingRow(bool asyncRead, bool partialReads)
    {
        string text = new('中', 32767);
        byte[] xml = Encoding.UTF8.GetBytes($"""
            <worksheet><sheetData>
            <row r="1"><c r="A1" t="inlineStr"><is><t>{text}</t></is></c><c r="B1"><v>42</v></c></row>
            <row r="2"><c r="A2"><v>43</v></c></row>
            </sheetData></worksheet>
            """);
        using var stream = new PartialReadStream(xml, partialReads ? 4093 : int.MaxValue);
        using var cursor = XlsxSheetScanner.OpenCursor(stream, SharedStringTable.Empty,
            StyleTable.Default, false, ReadMode.Values, ExcelRange.Unbounded);
        var rows = new List<ExcelRow>();
        int attempts = 0;
        while (attempts++ < 50)
        {
            if (asyncRead)
            {
                if (cursor.TryParseNext(out var row))
                {
                    rows.Add(row.ToSnapshot());
                    continue;
                }
                if (cursor.IsSheetDone) { break; }
                if (!await cursor.RefillAsync(TestContext.Current.CancellationToken)) { break; }
            }
            else
            {
                if (!cursor.MoveNext()) { break; }
                rows.Add(cursor.Current.ToSnapshot());
            }
        }

        Assert.True(attempts < 50, "The parse/refill loop must make progress.");
        Assert.Equal(2, rows.Count);
        Assert.Equal(text, rows[0].GetCell(1).AsText());
        Assert.Equal(42, rows[0].GetCell(2).AsNumber());
        Assert.Equal(2, rows[1].RowIndex);
        Assert.Equal(43, rows[1].GetCell(1).AsNumber());
    }

    [Fact]
    public void ExtractUntilClose_UnterminatedToken_StopsAtBufferLimit()
    {
        using var stream = new MemoryStream(new byte[16 * 1024 * 1024 + 1]);
        using var buffer = new ScanBuffer(stream);

        Assert.Throws<MalformedWorkbookException>(() =>
        {
            XmlByteReader.ExtractUntilClose(buffer, "t"u8);
        });
    }

    private sealed class PartialReadStream(byte[] bytes, int chunkSize) : MemoryStream(bytes)
    {
        public override int Read(byte[] buffer, int offset, int count) =>
            base.Read(buffer, offset, Math.Min(count, chunkSize));

        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) =>
            base.ReadAsync(buffer[..Math.Min(buffer.Length, chunkSize)], cancellationToken);
    }
}
