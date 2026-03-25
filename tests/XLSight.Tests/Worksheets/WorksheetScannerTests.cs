using System.Text;
using Xunit;
using XLSight.Models;
using XLSight.Models.Analysis;
using XLSight.Worksheets;

namespace XLSight.Tests.Worksheets;

public sealed class WorksheetScannerTests
{
    private const string Ns = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";

    private static MemoryStream XmlStream(string xml) =>
        new MemoryStream(Encoding.UTF8.GetBytes(xml));

    private struct MockSink : IWorksheetSink
    {
        public ExcelRange? Dimension;
        public List<int> RowStarts = new();
        public List<ParsedCell> Cells = new();
        public List<ExcelMergedRegion> MergedRegions = new();
        public bool Ended;
        public bool StopAfterFirstCell;

        public MockSink() { }

        public void OnDimension(in ExcelRange d) => Dimension = d;
        public void OnRowStart(int r) => RowStarts.Add(r);
        public bool OnCell(in ParsedCell c) { Cells.Add(c); return !StopAfterFirstCell; }
        public void OnMergeCell(in ExcelMergedRegion r) => MergedRegions.Add(r);
        public void OnEnd() => Ended = true;
    }

    private sealed class ClassMockSink : IWorksheetSink
    {
        public ExcelRange? Dimension;
        public List<int> RowStarts = new();
        public List<ParsedCell> Cells = new();
        public List<ExcelMergedRegion> MergedRegions = new();
        public bool Ended;

        public void OnDimension(in ExcelRange d) => Dimension = d;
        public void OnRowStart(int r) => RowStarts.Add(r);
        public bool OnCell(in ParsedCell c) { Cells.Add(c); return true; }
        public void OnMergeCell(in ExcelMergedRegion r) => MergedRegions.Add(r);
        public void OnEnd() => Ended = true;
    }

    [Fact]
    public void Scan_Dimension_CallsOnDimension()
    {
        var xml = $"""
            <worksheet xmlns="{Ns}">
              <dimension ref="A1:C3"/>
              <sheetData/>
            </worksheet>
            """;
        var names = new XlsxNameTable();
        var sink = new MockSink();

        using var stream = XmlStream(xml);
        WorksheetScanner.Scan(stream, names, ref sink);

        Assert.NotNull(sink.Dimension);
        Assert.Equal(new ExcelAddress(1, 1), sink.Dimension!.Value.TopLeft);
        Assert.Equal(new ExcelAddress(3, 3), sink.Dimension!.Value.BottomRight);
        Assert.True(sink.Ended);
    }

    [Fact]
    public void Scan_NumberCell_ParsesRowColumnValue()
    {
        var xml = $"""
            <worksheet xmlns="{Ns}">
              <sheetData>
                <row r="1"><c r="B2" s="0"><v>42</v></c></row>
              </sheetData>
            </worksheet>
            """;
        var names = new XlsxNameTable();
        var sink = new MockSink();

        using var stream = XmlStream(xml);
        WorksheetScanner.Scan(stream, names, ref sink);

        Assert.Single(sink.Cells);
        var cell = sink.Cells[0];
        Assert.Equal(2, cell.Row);
        Assert.Equal(2, cell.Column);
        Assert.Equal(CellDataKind.Number, cell.DataKind);
        Assert.Equal("42", cell.RawValue);
    }

    [Fact]
    public void Scan_SharedStringCell_ParsesKindAndValue()
    {
        var xml = $"""
            <worksheet xmlns="{Ns}">
              <sheetData>
                <row r="1"><c r="A1" t="s"><v>0</v></c></row>
              </sheetData>
            </worksheet>
            """;
        var names = new XlsxNameTable();
        var sink = new MockSink();

        using var stream = XmlStream(xml);
        WorksheetScanner.Scan(stream, names, ref sink);

        Assert.Single(sink.Cells);
        var cell = sink.Cells[0];
        Assert.Equal(CellDataKind.SharedString, cell.DataKind);
        Assert.Equal("0", cell.RawValue);
    }

    [Fact]
    public void Scan_EmptyVElement_SharedString_RawValueIsEmpty()
    {
        var xml = $"""
            <worksheet xmlns="{Ns}">
              <sheetData>
                <row r="1"><c r="A1" t="s"><v/></c></row>
              </sheetData>
            </worksheet>
            """;
        var names = new XlsxNameTable();
        var sink = new MockSink();

        using var stream = XmlStream(xml);
        WorksheetScanner.Scan(stream, names, ref sink);

        Assert.Single(sink.Cells);
        var cell = sink.Cells[0];
        Assert.Equal(CellDataKind.SharedString, cell.DataKind);
        Assert.True(string.IsNullOrEmpty(cell.RawValue));
    }

    [Fact]
    public void Scan_BooleanCell_ParsesCorrectly()
    {
        var xml = $"""
            <worksheet xmlns="{Ns}">
              <sheetData>
                <row r="1"><c r="A1" t="b"><v>1</v></c></row>
              </sheetData>
            </worksheet>
            """;
        var names = new XlsxNameTable();
        var sink = new MockSink();

        using var stream = XmlStream(xml);
        WorksheetScanner.Scan(stream, names, ref sink);

        Assert.Single(sink.Cells);
        var cell = sink.Cells[0];
        Assert.Equal(CellDataKind.Boolean, cell.DataKind);
        Assert.Equal("1", cell.RawValue);
    }

    [Fact]
    public void Scan_InlineStringCell_ParsesInlineString()
    {
        var xml = $"""
            <worksheet xmlns="{Ns}">
              <sheetData>
                <row r="1"><c r="A1" t="inlineStr"><is><t>Hello World</t></is></c></row>
              </sheetData>
            </worksheet>
            """;
        var names = new XlsxNameTable();
        var sink = new MockSink();

        using var stream = XmlStream(xml);
        WorksheetScanner.Scan(stream, names, ref sink);

        Assert.Single(sink.Cells);
        var cell = sink.Cells[0];
        Assert.Equal(CellDataKind.InlineString, cell.DataKind);
        Assert.Equal("Hello World", cell.InlineString);
    }

    [Fact]
    public void Scan_FormulaCell_ParsesFormula()
    {
        var xml = $"""
            <worksheet xmlns="{Ns}">
              <sheetData>
                <row r="1"><c r="A1"><f>SUM(A1:B1)</f><v>42</v></c></row>
              </sheetData>
            </worksheet>
            """;
        var names = new XlsxNameTable();
        var sink = new MockSink();

        using var stream = XmlStream(xml);
        WorksheetScanner.Scan(stream, names, ref sink);

        Assert.Single(sink.Cells);
        var cell = sink.Cells[0];
        Assert.Equal("SUM(A1:B1)", cell.FormulaText);
        Assert.Equal("42", cell.RawValue);
    }

    [Fact]
    public void Scan_EarlyTermination_StopsOnFalseFromOnCell()
    {
        var xml = $"""
            <worksheet xmlns="{Ns}">
              <sheetData>
                <row r="1">
                  <c r="A1"><v>1</v></c>
                  <c r="B1"><v>2</v></c>
                  <c r="C1"><v>3</v></c>
                </row>
              </sheetData>
            </worksheet>
            """;
        var names = new XlsxNameTable();
        var sink = new MockSink { StopAfterFirstCell = true };

        using var stream = XmlStream(xml);
        WorksheetScanner.Scan(stream, names, ref sink);

        Assert.Single(sink.Cells);
        Assert.False(sink.Ended);
    }

    [Fact]
    public void Scan_UnknownElements_SkippedSilently()
    {
        var xml = $"""
            <worksheet xmlns="{Ns}">
              <sheetViews><sheetView/></sheetViews>
              <sheetData>
                <row r="1"><c r="A1"><v>42</v></c></row>
              </sheetData>
            </worksheet>
            """;
        var names = new XlsxNameTable();
        var sink = new MockSink();

        using var stream = XmlStream(xml);
        WorksheetScanner.Scan(stream, names, ref sink);

        Assert.Single(sink.Cells);
        Assert.Equal("42", sink.Cells[0].RawValue);
    }

    [Fact]
    public void Scan_MergeCell_CallsOnMergeCell()
    {
        var xml = $"""
            <worksheet xmlns="{Ns}">
              <sheetData/>
              <mergeCells>
                <mergeCell ref="A1:B2"/>
              </mergeCells>
            </worksheet>
            """;
        var names = new XlsxNameTable();
        var sink = new MockSink();

        using var stream = XmlStream(xml);
        WorksheetScanner.Scan(stream, names, ref sink);

        Assert.Single(sink.MergedRegions);
        var region = sink.MergedRegions[0];
        Assert.Equal(1, region.StartRow);
        Assert.Equal(1, region.StartColumn);
        Assert.Equal(2, region.EndRow);
        Assert.Equal(2, region.EndColumn);
    }

    [Fact]
    public void Scan_MultipleRows_CallsOnRowStartForEach()
    {
        var xml = $"""
            <worksheet xmlns="{Ns}">
              <sheetData>
                <row r="1"><c r="A1"><v>1</v></c></row>
                <row r="2"><c r="A2"><v>2</v></c></row>
              </sheetData>
            </worksheet>
            """;
        var names = new XlsxNameTable();
        var sink = new MockSink();

        using var stream = XmlStream(xml);
        WorksheetScanner.Scan(stream, names, ref sink);

        Assert.Equal([1, 2], sink.RowStarts);
    }

    [Fact]
    public async Task ScanAsync_NumberCell_SameResultAsSync()
    {
        var xml = $"""
            <worksheet xmlns="{Ns}">
              <sheetData>
                <row r="1"><c r="B2" s="0"><v>42</v></c></row>
              </sheetData>
            </worksheet>
            """;
        var names = new XlsxNameTable();
        var sink = new ClassMockSink();

        using var stream = XmlStream(xml);
        await WorksheetScanner.ScanAsync(stream, names, sink, TestContext.Current.CancellationToken);

        Assert.Single(sink.Cells);
        var cell = sink.Cells[0];
        Assert.Equal(2, cell.Row);
        Assert.Equal(2, cell.Column);
        Assert.Equal(CellDataKind.Number, cell.DataKind);
        Assert.Equal("42", cell.RawValue);
        Assert.True(sink.Ended);
    }

    // Bug 1: ReadValueChunk must be drained in a loop — values longer than the
    // 256-char pool buffer must not be silently truncated.
    [Fact]
    public void Scan_LongValue_ExceedingBuffer_IsReadCompletely()
    {
        var longValue = new string('A', 600); // well beyond the 256-char ArrayPool buffer
        var xml = $"""
            <worksheet xmlns="{Ns}">
              <sheetData>
                <row r="1"><c r="A1" t="str"><v>{longValue}</v></c></row>
              </sheetData>
            </worksheet>
            """;
        var names = new XlsxNameTable();
        var sink = new MockSink();

        using var stream = XmlStream(xml);
        WorksheetScanner.Scan(stream, names, ref sink);

        Assert.Single(sink.Cells);
        Assert.Equal(longValue, sink.Cells[0].RawValue);
    }

    // Bug 2: RawValue must be a safe string, not aliased into the shared pool
    // buffer.  Storing ParsedCell and reading RawValue after Scan() must work.
    [Fact]
    public void Scan_RawValue_IsNotAliasedToPoolBuffer()
    {
        var xml = $"""
            <worksheet xmlns="{Ns}">
              <sheetData>
                <row r="1">
                  <c r="A1"><v>first</v></c>
                  <c r="B1"><v>second</v></c>
                </row>
              </sheetData>
            </worksheet>
            """;
        var names = new XlsxNameTable();
        var sink = new MockSink();

        using var stream = XmlStream(xml);
        WorksheetScanner.Scan(stream, names, ref sink);

        // Both values must be intact after Scan() has returned and the pool
        // buffer has been released.
        Assert.Equal(2, sink.Cells.Count);
        Assert.Equal("first", sink.Cells[0].RawValue);
        Assert.Equal("second", sink.Cells[1].RawValue);
    }

    // Bug 3: rows with no r= attribute must be assigned sequential row numbers.
    [Fact]
    public void Scan_RowWithoutRAttribute_InfersRowNumber()
    {
        var xml = $"""
            <worksheet xmlns="{Ns}">
              <sheetData>
                <row><c r="A1"><v>10</v></c></row>
                <row><c r="A2"><v>20</v></c></row>
              </sheetData>
            </worksheet>
            """;
        var names = new XlsxNameTable();
        var sink = new MockSink();

        using var stream = XmlStream(xml);
        WorksheetScanner.Scan(stream, names, ref sink);

        Assert.Equal([1, 2], sink.RowStarts);
    }

    [Fact]
    public void Scan_MixedRowAttributePresence_InfersCorrectly()
    {
        var xml = $"""
            <worksheet xmlns="{Ns}">
              <sheetData>
                <row r="3"><c r="A3"><v>30</v></c></row>
                <row><c r="A4"><v>40</v></c></row>
                <row><c r="A5"><v>50</v></c></row>
              </sheetData>
            </worksheet>
            """;
        var names = new XlsxNameTable();
        var sink = new MockSink();

        using var stream = XmlStream(xml);
        WorksheetScanner.Scan(stream, names, ref sink);

        Assert.Equal([3, 4, 5], sink.RowStarts);
    }
}
