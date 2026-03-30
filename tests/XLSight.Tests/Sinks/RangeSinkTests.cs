using System.Text;
using XLSight.Internal.Metadata;
using XLSight.Internal.Readers.Xlsx;
using XLSight.Internal.Sinks;
using XLSight.Models;
using XLSight.Models.Analysis;
using XLSight.Tests.Infrastructure;
using Xunit;

namespace XLSight.Tests.Sinks;

public sealed class RangeSinkTests
{
    private static MemoryStream WorksheetXml(string sheetDataXml) =>
        new MemoryStream(System.Text.Encoding.UTF8.GetBytes(
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
            ReadMode.Values,
            range,
            ref sink);
        return buffer;
    }

    private static ExcelRange Range(int col1, int row1, int col2, int row2) =>
        new ExcelRange(new ExcelAddress(col1, row1), new ExcelAddress(col2, row2));

    // ── Original tests ────────────────────────────────────────────────────────

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
        var buffer = ScanRange(stream, Range(2, 2, 3, 2));

        Assert.Equal(2, buffer.Length);
        Assert.Equal(ExcelCellValue.FromNumber(7), buffer[0]);
        Assert.Equal(ExcelCellValue.FromNumber(8), buffer[1]);
    }

    // ── Branch coverage additions ─────────────────────────────────────────────

    [Fact]
    public void Scan_RowBeforeRangeStart_CellNotWrittenToBuffer()
    {
        using var stream = WorksheetXml("""
            <row r="1"><c r="A1"><v>99</v></c></row>
            <row r="3"><c r="A3"><v>42</v></c></row>
            """);
        var buffer = ScanRange(stream, Range(1, 3, 1, 3));

        Assert.Single(buffer);
        Assert.Equal(ExcelCellValue.FromNumber(42), buffer[0]);
    }

    [Fact]
    public void Scan_LargeBoundedRange_ReadsSparsedRows()
    {
        using var stream = WorksheetXml("""
            <row r="1"><c r="A1"><v>10</v></c></row>
            <row r="2"><c r="A2"><v>20</v></c></row>
            <row r="50"><c r="A50"><v>500</v></c></row>
            """);
        var bigRange = Range(1, 1, 1, 100);
        var buffer = ScanRange(stream, bigRange);

        Assert.Equal(ExcelCellValue.FromNumber(10), buffer[0]);
        Assert.Equal(ExcelCellValue.FromNumber(20), buffer[1]);
        Assert.Equal(ExcelCellValue.FromNumber(500), buffer[49]);
    }

    [Fact]
    public void Scan_ColumnBeforeRangeStart_NotWrittenToBuffer()
    {
        using var stream = WorksheetXml("""
            <row r="1">
              <c r="A1"><v>1</v></c>
              <c r="B1"><v>2</v></c>
              <c r="C1"><v>3</v></c>
              <c r="D1"><v>4</v></c>
            </row>
            """);
        var buffer = ScanRange(stream, Range(3, 1, 3, 1));

        Assert.Single(buffer);
        Assert.Equal(ExcelCellValue.FromNumber(3), buffer[0]);
    }

    [Fact]
    public void RangeSink_NeedsDecodedValue_IsTrue()
    {
        var buffer = new ExcelCellValue[1];
        var sink = new RangeSink(Range(1, 1, 1, 1), buffer);
        Assert.True(sink.NeedsDecodedValue);
    }

    [Fact]
    public void RangeSink_TracksFormulas_IsFalse()
    {
        var buffer = new ExcelCellValue[1];
        var sink = new RangeSink(Range(1, 1, 1, 1), buffer);
        Assert.False(sink.TracksFormulas);
    }

    // ── Direct method invocation (scanner-filtered paths) ────────────────────

    [Fact]
    public void RangeSink_OnRowStart_BeyondRange_SetsPastEndSoOnCellReturnsFalse()
    {
        var buffer = new ExcelCellValue[1];
        var sink = new RangeSink(Range(1, 1, 1, 1), buffer);
        sink.OnRowStart(2); // row 2 > bottomRight.Row (1) → _pastEnd = true
        bool result = sink.OnCell(1, CellDataKind.Number, 0, ExcelCellValue.FromNumber(99.0), -1);
        Assert.False(result);
    }

    [Fact]
    public void RangeSink_OnFormula_DoesNotThrow()
    {
        var buffer = new ExcelCellValue[1];
        var sink = new RangeSink(Range(1, 1, 1, 1), buffer);
        sink.OnFormula(1, isArray: false);
        sink.OnFormula(1, isArray: true);
    }

    [Fact]
    public void RangeSink_NoOpMethods_DoNotThrow()
    {
        var buffer = new ExcelCellValue[1];
        var sink = new RangeSink(Range(1, 1, 1, 1), buffer);
        sink.OnMergeCell(new MergedRegion(1, 1, 2, 2));
        sink.OnConditionalFormatting();
        sink.OnDataValidation();
        sink.OnHyperlink();
        sink.OnEnd();
        sink.OnDimension(Range(1, 1, 5, 5));
    }

    [Fact]
    public void RangeSink_OnRowStart_UnboundedRange_SkipsPastEndCheck()
    {
        // Unbounded range: _range.IsUnbounded = true → _pastEnd check is skipped
        var buffer = new ExcelCellValue[100];
        var sink = new RangeSink(ExcelRange.Unbounded, buffer);
        sink.OnRowStart(1000);
        sink.OnRowStart(1);
    }
}
