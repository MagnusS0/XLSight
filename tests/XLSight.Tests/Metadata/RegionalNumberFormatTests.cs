using System.Buffers.Binary;
using System.Text;
using XLSight.Internal.Metadata;
using XLSight.Internal.Readers.Xlsb;
using XLSight.Internal.Readers.Xlsx;
using XLSight.Tests.Readers.Xlsb;
using Xunit;

namespace XLSight.Tests.Metadata;

public sealed class RegionalNumberFormatTests
{
    [Theory]
    [InlineData(27)]
    [InlineData(28)]
    [InlineData(29)]
    [InlineData(30)]
    [InlineData(31)]
    [InlineData(32)]
    [InlineData(33)]
    [InlineData(34)]
    [InlineData(35)]
    [InlineData(36)]
    [InlineData(50)]
    [InlineData(51)]
    [InlineData(52)]
    [InlineData(53)]
    [InlineData(54)]
    [InlineData(55)]
    [InlineData(56)]
    [InlineData(57)]
    [InlineData(58)]
    public void RegionalBuiltInFormat_DecodesTemporalValueInXlsxAndXlsb(int formatId)
    {
        var expected = new DateTime(2023, 1, 1, 12, 0, 0);
        using var xlsxStyles = new MemoryStream(Encoding.UTF8.GetBytes($"""
            <styleSheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main">
              <cellXfs><xf numFmtId="{formatId}" /></cellXfs>
            </styleSheet>
            """));
        using var xlsxSheet = new MemoryStream("<worksheet><sheetData><row r=\"1\"><c r=\"A1\"><v>44927.5</v></c></row></sheetData></worksheet>"u8.ToArray());
        using var xlsxCursor = XlsxSheetScanner.OpenCursor(
            xlsxSheet, SharedStringTable.Empty, StylesParser.Parse(xlsxStyles),
            isDate1904: false, ReadMode.Values, ExcelRange.Unbounded);

        Assert.True(xlsxCursor.MoveNext());
        Assert.Equal(expected, xlsxCursor.Current.GetCell(1).AsDate());

        var xf = new byte[4];
        BinaryPrimitives.WriteUInt16LittleEndian(xf.AsSpan(2), (ushort)formatId);
        using var xlsbStyles = XlsbTestRecords.Stream(
            XlsbTestRecords.Record(XlsbRecordType.BrtBeginCellXFs, []),
            XlsbTestRecords.Record(XlsbRecordType.BrtXF, xf),
            XlsbTestRecords.Record(XlsbRecordType.BrtEndCellXFs, []));
        using var xlsbSheet = XlsbTestRecords.Stream(
            XlsbTestRecords.Row(1), XlsbTestRecords.Real(1, 44927.5), XlsbTestRecords.EndSheetData());
        using var xlsbCursor = new XlsbSheetCursor(
            xlsbSheet, new Lazy<XlsbSharedStringTable>(() => XlsbSharedStringTable.Empty),
            XlsbStylesParser.Parse(xlsbStyles), isDate1904: false, ReadMode.Values, ExcelRange.Unbounded);

        Assert.True(xlsbCursor.MoveNext());
        Assert.Equal(expected, xlsbCursor.Current.GetCell(1).AsDate());
    }
}
