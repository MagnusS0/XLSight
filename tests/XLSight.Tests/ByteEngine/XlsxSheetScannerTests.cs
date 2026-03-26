using System.Text;
using Xunit;
using XLSight.ByteEngine;
using XLSight.Infrastructure;
using XLSight.Models;
using XLSight.Packaging;
using XLSight.SharedStrings;
using XLSight.Styles;
using XLSight.Worksheets;

namespace XLSight.Tests.ByteEngine;

/// <summary>
/// Correctness tests for <see cref="XlsxSheetScanner"/>.
/// Unit tests use synthetic XML. Parity tests compare against
/// <see cref="WorksheetScanner.ScanRows"/> on real fixture files.
/// </summary>
public sealed class XlsxSheetScannerTests
{
    private const string Ns = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";

    private static MemoryStream XmlStream(string xml)
        => new(Encoding.UTF8.GetBytes(xml));

    // ── Basic decoding ───────────────────────────────────────────────────────

    [Fact]
    public void NumberCell_ReturnsCorrectValue()
    {
        var rows = Scan($"""
            <worksheet xmlns="{Ns}">
              <sheetData>
                <row r="1"><c r="A1"><v>42</v></c></row>
              </sheetData>
            </worksheet>
            """);

        Assert.Single(rows);
        Assert.Equal(1, rows[0].RowIndex);
        Assert.Equal(42.0, rows[0].GetCell(1).AsNumber());
    }

    [Fact]
    public void SharedStringCell_LooksUpSst()
    {
        var rows = Scan($"""
            <worksheet xmlns="{Ns}">
              <sheetData>
                <row r="1"><c r="A1" t="s"><v>0</v></c></row>
              </sheetData>
            </worksheet>
            """, sst: ["Hello"]);

        Assert.Single(rows);
        Assert.Equal("Hello", rows[0].GetCell(1).AsText());
    }

    [Fact]
    public void BooleanCells_TrueAndFalse()
    {
        var rows = Scan($"""
            <worksheet xmlns="{Ns}">
              <sheetData>
                <row r="1">
                  <c r="A1" t="b"><v>1</v></c>
                  <c r="B1" t="b"><v>0</v></c>
                </row>
              </sheetData>
            </worksheet>
            """);

        Assert.True(rows[0].GetCell(1).AsBoolean());
        Assert.False(rows[0].GetCell(2).AsBoolean());
    }

    [Fact]
    public void ErrorCell_ReturnsErrorValue()
    {
        var rows = Scan($"""
            <worksheet xmlns="{Ns}">
              <sheetData>
                <row r="1"><c r="A1" t="e"><v>#DIV/0!</v></c></row>
              </sheetData>
            </worksheet>
            """);

        var cell = rows[0].GetCell(1);
        Assert.Equal(ExcelCellType.Error, cell.CellType);
        Assert.Equal("#DIV/0!", cell.AsError());
    }

    [Fact]
    public void InlineStringCell_ReturnsText()
    {
        var rows = Scan($"""
            <worksheet xmlns="{Ns}">
              <sheetData>
                <row r="1">
                  <c r="A1" t="inlineStr"><is><t>Inline text</t></is></c>
                </row>
              </sheetData>
            </worksheet>
            """);

        Assert.Equal("Inline text", rows[0].GetCell(1).AsText());
    }

    [Fact]
    public void FormulaStringCell_ReturnsText()
    {
        var rows = Scan($"""
            <worksheet xmlns="{Ns}">
              <sheetData>
                <row r="1"><c r="A1" t="str"><v>Result</v></c></row>
              </sheetData>
            </worksheet>
            """);

        Assert.Equal("Result", rows[0].GetCell(1).AsText());
    }

    // ── Edge cases ───────────────────────────────────────────────────────────

    [Fact]
    public void EmptySheetData_YieldsNoRows()
    {
        var rows = Scan($"""
            <worksheet xmlns="{Ns}">
              <sheetData/>
            </worksheet>
            """);

        Assert.Empty(rows);
    }

    [Fact]
    public void SharedStringCell_EmptyV_ReturnsEmpty()
    {
        // t="s" with <v/> must NOT return sharedStrings[0].
        var rows = Scan($"""
            <worksheet xmlns="{Ns}">
              <sheetData>
                <row r="1"><c r="A1" t="s"><v/></c></row>
              </sheetData>
            </worksheet>
            """, sst: ["ShouldNotAppear"]);

        Assert.Single(rows);
        Assert.True(rows[0].GetCell(1).IsEmpty);
    }

    [Fact]
    public void EmptyElementCell_ProducesEmptyValue()
    {
        var rows = Scan($"""
            <worksheet xmlns="{Ns}">
              <sheetData>
                <row r="1"><c r="A1"/><c r="B1"><v>7</v></c></row>
              </sheetData>
            </worksheet>
            """);

        Assert.Single(rows);
        Assert.True(rows[0].GetCell(1).IsEmpty);
        Assert.Equal(7.0, rows[0].GetCell(2).AsNumber());
    }

    [Fact]
    public void AbsentR_Attribute_UsesSequentialColumnTracking()
    {
        var rows = Scan($"""
            <worksheet xmlns="{Ns}">
              <sheetData>
                <row r="1">
                  <c><v>10</v></c>
                  <c><v>20</v></c>
                  <c><v>30</v></c>
                </row>
              </sheetData>
            </worksheet>
            """);

        Assert.Single(rows);
        Assert.Equal(10.0, rows[0].GetCell(1).AsNumber());
        Assert.Equal(20.0, rows[0].GetCell(2).AsNumber());
        Assert.Equal(30.0, rows[0].GetCell(3).AsNumber());
    }

    [Fact]
    public void MultipleRows_AllYielded()
    {
        var rows = Scan($"""
            <worksheet xmlns="{Ns}">
              <sheetData>
                <row r="1"><c r="A1"><v>1</v></c></row>
                <row r="2"><c r="A2"><v>2</v></c></row>
                <row r="3"><c r="A3"><v>3</v></c></row>
              </sheetData>
            </worksheet>
            """);

        Assert.Equal(3, rows.Count);
        for (int i = 0; i < 3; i++)
        {
            Assert.Equal(i + 1, rows[i].RowIndex);
            Assert.Equal(i + 1.0, rows[i].GetCell(1).AsNumber());
        }
    }

    [Fact]
    public void XmlEntitiesInString_AreUnescaped()
    {
        var rows = Scan($"""
            <worksheet xmlns="{Ns}">
              <sheetData>
                <row r="1">
                  <c r="A1" t="str"><v>&amp;lt;&amp;gt;</v></c>
                </row>
              </sheetData>
            </worksheet>
            """);

        // The raw XML &amp; → & after first XML parse by browser, but here
        // we're scanning raw bytes so entity is literal &amp;lt; → &lt; in the value bytes.
        // The formula string decode path calls UnescapeXml, so &amp; → &.
        Assert.Equal(ExcelCellType.Text, rows[0].GetCell(1).CellType);
    }

    // ── Range filtering ──────────────────────────────────────────────────────

    [Fact]
    public void BoundedRange_SkipsRowsOutsideRange()
    {
        var range = new ExcelRange(new ExcelAddress(1, 2), new ExcelAddress(1, 3));
        var rows = Scan($"""
            <worksheet xmlns="{Ns}">
              <sheetData>
                <row r="1"><c r="A1"><v>1</v></c></row>
                <row r="2"><c r="A2"><v>2</v></c></row>
                <row r="3"><c r="A3"><v>3</v></c></row>
                <row r="4"><c r="A4"><v>4</v></c></row>
              </sheetData>
            </worksheet>
            """, range: range);

        Assert.Equal(2, rows.Count);
        Assert.Equal(2, rows[0].RowIndex);
        Assert.Equal(3, rows[1].RowIndex);
    }

    [Fact]
    public void BoundedRange_SkipsColumnsOutsideRange()
    {
        // Only column B (index 2).
        var range = new ExcelRange(new ExcelAddress(2, 1), new ExcelAddress(2, 1));
        var rows = Scan($"""
            <worksheet xmlns="{Ns}">
              <sheetData>
                <row r="1">
                  <c r="A1"><v>10</v></c>
                  <c r="B1"><v>20</v></c>
                  <c r="C1"><v>30</v></c>
                </row>
              </sheetData>
            </worksheet>
            """, range: range);

        Assert.Single(rows);
        Assert.Equal(20.0, rows[0].GetCell(2).AsNumber());
    }

    // ── Early exit ───────────────────────────────────────────────────────────

    [Fact]
    public void TakeN_DisposesCleanlyAndReturnsExactCount()
    {
        var rows = Scan($"""
            <worksheet xmlns="{Ns}">
              <sheetData>
                <row r="1"><c r="A1"><v>1</v></c></row>
                <row r="2"><c r="A2"><v>2</v></c></row>
                <row r="3"><c r="A3"><v>3</v></c></row>
              </sheetData>
            </worksheet>
            """).Take(2).ToList();

        Assert.Equal(2, rows.Count);
    }

    // ── Parity against WorksheetScanner.ScanRows ─────────────────────────────

    [Theory]
    [InlineData("small.xlsx")]
    [InlineData("string_heavy.xlsx")]
    [InlineData("medium.xlsx")]
    public void RealFile_ByteEngineMatchesXmlEngine(string fileName)
    {
        var path = Path.Combine(AppContext.BaseDirectory, "TestData", fileName);
        if (!File.Exists(path)) { return; }

        var xmlRows = StreamWithXmlEngine(path).ToList();
        var byteRows = StreamWithByteEngine(path).ToList();

        Assert.Equal(xmlRows.Count, byteRows.Count);
        for (int i = 0; i < xmlRows.Count; i++)
        {
            AssertRowsEqual(xmlRows[i], byteRows[i], fileName, i);
        }
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private static List<ExcelRow> Scan(
        string worksheetXml,
        string[]? sst = null,
        ExcelRange? range = null)
    {
        using var stream = XmlStream(worksheetXml);
        return XlsxSheetScanner.ScanRows(
            stream,
            sst ?? [],
            StyleTable.Default,
            isDate1904: false,
            ExcelReadMode.Values,
            range ?? ExcelRange.Unbounded).ToList();
    }

    private static List<ExcelRow> StreamWithXmlEngine(string path)
    {
        using var wb = global::XLSight.ExcelWorkbook.Open(path);
        return wb.StreamSheet(wb.SheetNames[0]).ToList();
    }

    private static List<ExcelRow> StreamWithByteEngine(string path)
    {
        using var package = XlsxPackage.Open(File.OpenRead(path), ownsStream: true);
        var names = new XlsxNameTable();

        using var wbStream = package.GetEntry("xl/workbook.xml")!.OpenBuffered();
        using var relsStream = package.GetEntry("xl/_rels/workbook.xml.rels")!.OpenBuffered();
        var def = WorkbookParser.Parse(wbStream);
        var metadata = RelationshipsParser.Parse(relsStream, def);

        string[] sst = [];
        var sstEntry = package.GetEntry("xl/sharedStrings.xml");
        if (sstEntry is not null)
        {
            using var sstStream = sstEntry.OpenBuffered();
            sst = SharedStringsParser.Parse(sstStream, names);
        }

        StyleTable styles = StyleTable.Default;
        var stylesEntry = package.GetEntry("xl/styles.xml");
        if (stylesEntry is not null)
        {
            using var stylesStream = stylesEntry.OpenBuffered();
            styles = StylesParser.Parse(stylesStream, names);
        }

        var sheet = metadata.Sheets[0];
        var wsEntry = package.GetEntry(sheet.Path);
        if (wsEntry is null) { return []; }

        using var wsStream = wsEntry.OpenBuffered();
        return XlsxSheetScanner.ScanRows(
            wsStream, sst, styles, metadata.UsesDate1904,
            ExcelReadMode.Values, ExcelRange.Unbounded).ToList();
    }

    private static void AssertRowsEqual(ExcelRow expected, ExcelRow actual, string file, int rowIdx)
    {
        Assert.True(
            expected.RowIndex == actual.RowIndex,
            $"{file} row[{rowIdx}]: RowIndex {expected.RowIndex} != {actual.RowIndex}");
        Assert.True(
            expected.StartColumn == actual.StartColumn,
            $"{file} row[{rowIdx}]: StartColumn {expected.StartColumn} != {actual.StartColumn}");
        Assert.True(
            expected.CellCount == actual.CellCount,
            $"{file} row[{rowIdx}]: CellCount {expected.CellCount} != {actual.CellCount}");
        for (int col = expected.StartColumn; col < expected.StartColumn + expected.CellCount; col++)
        {
            var exp = expected.GetCell(col);
            var act = actual.GetCell(col);
            Assert.True(
                exp.CellType == act.CellType,
                $"{file} row[{rowIdx}] col {col}: CellType {exp.CellType} != {act.CellType}");
        }
    }
}
