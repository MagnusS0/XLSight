using XLSight.Tests.Infrastructure;
using System.Text;
using Xunit;
using XLSight.Internal.Readers.Xlsx;
using XLSight.Models;
using XLSight.Internal.Metadata;
using XLSight.Internal.Sinks;

namespace XLSight.Tests.Worksheets;

public sealed class RangeSinkTests
{
    private static Stream WorksheetXml(string sheetDataXml) =>
        new MemoryStream(Encoding.UTF8.GetBytes(
            $"""
            <worksheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main">
              <sheetData>{sheetDataXml}</sheetData>
            </worksheet>
            """));

    private static ExcelCellValue[] ScanRange(
        Stream stream,
        ExcelRange range,
        SharedStringTable? sharedStrings = null,
        StyleTable? styles = null)
    {
        var buffer = new ExcelCellValue[range.Width * range.Height];
        var sink = new RangeSink(range, buffer);
        XlsxSheetScanner.ScanSheet(
            stream,
            sharedStrings ?? SharedStringTable.Empty,
            styles ?? StyleTable.Default,
            isDate1904: false,
            range,
            ref sink);
        return buffer;
    }

    private static ExcelRange Range(int col1, int row1, int col2, int row2) =>
        new ExcelRange(new ExcelAddress(col1, row1), new ExcelAddress(col2, row2));

    [Fact]
    public void Scan_SingleCell_NumberValue_WritesToBuffer()
    {
        using var stream = WorksheetXml("""<row r="1"><c r="A1" s="0"><v>42</v></c></row>""");
        var buffer = ScanRange(stream, Range(1, 1, 1, 1));

        Assert.Single(buffer);
        Assert.Equal(ExcelCellValue.FromNumber(42.0), buffer[0]);
    }

    [Fact]
    public void Scan_TwoByTwoRange_AllCellsDecoded()
    {
        using var stream = WorksheetXml("""
            <row r="1">
              <c r="A1"><v>1</v></c>
              <c r="B1"><v>2</v></c>
            </row>
            <row r="2">
              <c r="A2"><v>3</v></c>
              <c r="B2"><v>4</v></c>
            </row>
            """);
        var buffer = ScanRange(stream, Range(1, 1, 2, 2));

        Assert.Equal(4, buffer.Length);
        Assert.Equal(ExcelCellValue.FromNumber(1), buffer[0]);
        Assert.Equal(ExcelCellValue.FromNumber(2), buffer[1]);
        Assert.Equal(ExcelCellValue.FromNumber(3), buffer[2]);
        Assert.Equal(ExcelCellValue.FromNumber(4), buffer[3]);
    }

    [Fact]
    public void Scan_CellsOutsideRange_NotWrittenToBuffer()
    {
        using var stream = WorksheetXml("""
            <row r="1">
              <c r="A1"><v>99</v></c>
              <c r="B1"><v>42</v></c>
              <c r="C1"><v>99</v></c>
            </row>
            """);
        // Range B1:B1 (col 2 only)
        var buffer = ScanRange(stream, Range(2, 1, 2, 1));

        Assert.Single(buffer);
        Assert.Equal(ExcelCellValue.FromNumber(42), buffer[0]);
    }

    [Fact]
    public void Scan_EmptyCellInRange_StaysEmpty()
    {
        using var stream = WorksheetXml("""
            <row r="1">
              <c r="A1"><v>1</v></c>
            </row>
            <row r="2">
            </row>
            """);
        var buffer = ScanRange(stream, Range(1, 1, 1, 2));

        Assert.Equal(2, buffer.Length);
        Assert.Equal(ExcelCellValue.FromNumber(1), buffer[0]);
        Assert.Equal(ExcelCellValue.Empty, buffer[1]);
    }

    [Fact]
    public void Scan_EarlyTermination_AfterLastRow()
    {
        using var stream = WorksheetXml("""
            <row r="1"><c r="A1"><v>1</v></c></row>
            <row r="2"><c r="A2"><v>2</v></c></row>
            <row r="3"><c r="A3"><v>3</v></c></row>
            """);
        // Range A1:A1 — only row 1 is in range
        var buffer = ScanRange(stream, Range(1, 1, 1, 1));

        Assert.Single(buffer);
        Assert.Equal(ExcelCellValue.FromNumber(1), buffer[0]);
    }

    [Fact]
    public void Scan_SharedStringCell_DecodesCorrectly()
    {
        using var stream = WorksheetXml("""<row r="1"><c r="A1" t="s"><v>0</v></c></row>""");
        var buffer = ScanRange(stream, Range(1, 1, 1, 1), sharedStrings: SstBuilder.Make("Hello"));

        Assert.Single(buffer);
        Assert.Equal(ExcelCellValue.FromText("Hello"), buffer[0]);
    }

    [Fact]
    public void Scan_BooleanCell_DecodesCorrectly()
    {
        using var stream = WorksheetXml("""<row r="1"><c r="A1" t="b"><v>1</v></c></row>""");
        var buffer = ScanRange(stream, Range(1, 1, 1, 1));

        Assert.Single(buffer);
        Assert.Equal(ExcelCellValue.FromBoolean(true), buffer[0]);
    }

    [Fact]
    public void Scan_SubRange_CorrectOffsets()
    {
        using var stream = WorksheetXml("""
            <row r="2">
              <c r="B2"><v>7</v></c>
              <c r="C2"><v>8</v></c>
            </row>
            """);
        // Range B2:C2 — col 2-3, row 2
        var buffer = ScanRange(stream, Range(2, 2, 3, 2));

        Assert.Equal(2, buffer.Length);
        Assert.Equal(ExcelCellValue.FromNumber(7), buffer[0]);
        Assert.Equal(ExcelCellValue.FromNumber(8), buffer[1]);
    }
}
