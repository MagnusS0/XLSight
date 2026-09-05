using System.Text;
using XLSight.Internal.Metadata;
using XLSight.Internal.Readers.Xlsx;
using XLSight.Internal.Sinks;
using XLSight.Tests.Infrastructure;
using Xunit;

namespace XLSight.Tests.Readers.Xlsx;

public sealed class CachedValueFastPathTests
{
    [Fact]
    public void ReadCell_ImmediateAndWhitespaceSeparatedValues_PreserveTypedValue()
    {
        (string Cell, ExcelCellValue Expected)[] cases =
        [
            ("<c><v>-1.25E+3</v></c>", ExcelCellValue.FromNumber(-1250)),
            ("<c><v> 42 </v></c>", ExcelCellValue.FromNumber(42)),
            ("<c><v></v></c>", ExcelCellValue.Empty),
            ("<c><v>invalid</v></c>", ExcelCellValue.Empty),
            ("<c t=\"b\"><v>1</v></c>", ExcelCellValue.FromBoolean(true)),
            ("<c t=\"b\"><v>0</v></c>", ExcelCellValue.FromBoolean(false)),
            ("<c t=\"b\"><v></v></c>", ExcelCellValue.Empty),
            ("<c t=\"e\"><v>#DIV/0!</v></c>", ExcelCellValue.FromError("#DIV/0!")),
            ("<c t=\"str\"><v>A&amp;B &#x4E2D;</v></c>", ExcelCellValue.FromText("A&B 中")),
            ("<c t=\"str\"><v></v></c>", ExcelCellValue.Empty),
            ("<c t=\"s\"><v>0</v></c>", ExcelCellValue.FromSharedString("shared", 0)),
            ("<c t=\"s\"><v></v></c>", ExcelCellValue.Empty),
            ("<c t=\"s\"><v>invalid</v></c>", ExcelCellValue.Empty),
            ("<c s=\"1\"><v>1</v></c>", ExcelCellValue.FromDate(new DateTime(1900, 1, 1))),
        ];

        foreach (var (cell, expected) in cases)
        {
            AssertCell(cell, expected);
            AssertCell(cell.Replace("<v>", "\n<v>", StringComparison.Ordinal), expected);
        }
    }

    [Theory]
    [InlineData("<c><v/></c>", false)]
    [InlineData("<c><v></v></c>", false)]
    [InlineData("<c><v>42</v ></c>", true)]
    [InlineData("<c><v>42</v> </c>", true)]
    [InlineData("<c><v >42</v></c>", true)]
    [InlineData("<c><f>6*7</f><v>42</v></c>", true)]
    [InlineData("<x:c><x:v>42</x:v></x:c>", true)]
    public void ReadCell_OtherBodyShapes_PreserveFallback(string cell, bool hasValue)
        => AssertCell(cell, hasValue ? ExcelCellValue.FromNumber(42) : ExcelCellValue.Empty);

    [Fact]
    public void ReadCell_ImmediateDate_Uses1904DateSystem()
        => AssertCell("<c s=\"1\"><v>1</v></c>", ExcelCellValue.FromDate(new DateTime(1904, 1, 2)), isDate1904: true);

    [Theory]
    [InlineData("<v>123.5</v></c>")]
    [InlineData("<v>123.5</v> </c>")]
    public void ReadCell_EveryBufferSplit_PreservesValueAndFollowingCell(string body)
    {
        for (int split = 1; split <= body.Length; split++)
        {
            using var stream = new InitialChunkStream(Encoding.UTF8.GetBytes(body + "<c>next</c>"), split);
            using var buffer = new ScanBuffer(stream);

            var value = XlsxSheetScanner.ReadCellValue(
                buffer, CellDataKind.Number, 0, SharedStringTable.Empty, StyleTable.Default, isDate1904: false);

            Assert.Equal(123.5, value.AsNumber());
            if (buffer.Span.IsEmpty) { buffer.Refill(); }
            Assert.Equal("<c>next</c>", Encoding.UTF8.GetString(buffer.Span));
        }
    }

    private static void AssertCell(string cell, ExcelCellValue expected, bool isDate1904 = false)
    {
        string xml = $"<worksheet xmlns:x=\"urn:test\"><sheetData><row>{cell}<c><v>99</v></c></row></sheetData></worksheet>";
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(xml));
        using var sharedStrings = SstBuilder.Make("shared");
        using var cursor = XlsxSheetScanner.OpenCursor(
            stream, sharedStrings, new StyleTable([FormatClass.General, FormatClass.Date]),
            isDate1904, ReadMode.Values, ExcelRange.Unbounded);

        Assert.True(cursor.MoveNext());
        var actual = cursor.Current.GetCell(1);
        Assert.Equal(expected, actual);
        Assert.Equal(expected.TryGetSharedStringId(out int expectedId), actual.TryGetSharedStringId(out int actualId));
        Assert.Equal(expectedId, actualId);
        Assert.Equal(99, cursor.Current.GetCell(2).AsNumber());
        Assert.False(cursor.MoveNext());
    }

    private sealed class InitialChunkStream(byte[] bytes, int initialLength) : MemoryStream(bytes)
    {
        private bool _firstRead = true;

        public override int Read(byte[] buffer, int offset, int count)
        {
            if (_firstRead)
            {
                _firstRead = false;
                count = Math.Min(count, initialLength);
            }

            return base.Read(buffer, offset, count);
        }
    }
}
