using System.IO.Compression;
using System.Text;
using Xunit;

namespace XLSight.Query.Tests;

/// <summary>
/// Covers the "external header" feature: the header row can sit ABOVE the queried FROM range.
/// In that case the range is purely the data window — every row inside it is a data row, and
/// rows physically between the header and the range top are neither scanned nor returned.
/// </summary>
public sealed class QueryExternalHeaderTests
{
    // Fixture: SalesWorkbook.Build(titleRow: true) puts a banner in row 1 and headers in row 2,
    // with the 10 data records starting at row 3. The queried range (rows 6-12) starts several
    // rows below the header, so records 0-2 (rows 3-5) are genuinely skipped, and records 3-9
    // (rows 6-12) form the data window.
    private const int HeaderRow = 2;
    private const string Range = "A6:F12";
    private const int RangeTopRow = 6;
    private const int RangeBottomRow = 12;

    private static bool IsInWindow(int recordIndex)
    {
        int row = SalesWorkbook.SheetRowOf(recordIndex, HeaderRow);
        return row >= RangeTopRow && row <= RangeBottomRow;
    }

    private static SalesRecord[] WindowRecords() =>
        [.. SalesWorkbook.Data.Where((d, i) => IsInWindow(i))];

    private static int[] WindowRowIndices() =>
        [.. Enumerable.Range(0, SalesWorkbook.Data.Length)
            .Where(IsInWindow)
            .Select(i => SalesWorkbook.SheetRowOf(i, HeaderRow))];

    [Fact]
    public void ExternalHeaderRow_BindsColumnNamesAndScansOnlyRangeRows()
    {
        int[] expectedRows = WindowRowIndices();

        using var ms = SalesWorkbook.Build(titleRow: true);
        using var workbook = ExcelWorkbook.Open(ms);

        QueryResult result = workbook
            .QueryRange(SalesWorkbook.SheetName, Range, HeaderRow)
            .Execute();

        Assert.Equal(SalesWorkbook.Headers, result.Columns);
        Assert.Equal(expectedRows, result.Rows.Select(r => r.SourceRowIndex!.Value));
    }

    [Fact]
    public async Task ExternalHeaderRow_ExecuteAsync_ReturnsOnlyRangeRows()
    {
        int[] expectedRows = WindowRowIndices();

        using var ms = SalesWorkbook.Build(titleRow: true);
        using var workbook = ExcelWorkbook.Open(ms);

        QueryResult result = await workbook
            .QueryRange(SalesWorkbook.SheetName, Range, HeaderRow)
            .ExecuteAsync(TestContext.Current.CancellationToken);

        Assert.Equal(SalesWorkbook.Headers, result.Columns);
        Assert.Equal(expectedRows, result.Rows.Select(r => r.SourceRowIndex!.Value));
    }

    [Fact]
    public void ExternalHeaderRow_Take_StopsScanBeforeReadingWholeWindow()
    {
        int[] expectedRows = [.. WindowRowIndices().Take(3)];

        using var ms = SalesWorkbook.Build(titleRow: true);
        using var workbook = ExcelWorkbook.Open(ms);

        QueryResult result = workbook
            .QueryRange(SalesWorkbook.SheetName, Range, HeaderRow)
            .Take(3)
            .Execute();

        // The window holds 7 rows; the scan must stop after the 3rd rather than reading the rest.
        Assert.Equal(3, result.Rows.Count);
        Assert.Equal(3, result.RowsScanned);
        Assert.Equal(expectedRows, result.Rows.Select(r => r.SourceRowIndex!.Value));
    }

    [Fact]
    public void ExternalHeaderRow_RowsScanned_ExcludesRowsAboveRangeTop()
    {
        int expectedCount = WindowRecords().Length;

        using var ms = SalesWorkbook.Build(titleRow: true);
        using var workbook = ExcelWorkbook.Open(ms);

        QueryResult result = workbook
            .QueryRange(SalesWorkbook.SheetName, Range, HeaderRow)
            .Select(QueryAggregates.Count())
            .Execute();

        Assert.Equal(expectedCount, result.RowsScanned);
        Assert.Equal(expectedCount, result.RowsMatched);
        Assert.Equal(expectedCount, result.Rows[0].Values.Span[0].AsNumber());
    }

    [Fact]
    public void ExternalHeaderRow_Aggregates_MatchLinqOverRangeRecordsOnly()
    {
        SalesRecord[] window = WindowRecords();
        double expectedSum = window.Sum(d => d.Units);
        int expectedCount = window.Length;

        using var ms = SalesWorkbook.Build(titleRow: true);
        using var workbook = ExcelWorkbook.Open(ms);

        QueryResult result = workbook
            .QueryRange(SalesWorkbook.SheetName, Range, HeaderRow)
            .Select(QueryAggregates.Sum("Units"), QueryAggregates.Count())
            .Execute();

        var row = Assert.Single(result.Rows);
        Assert.Equal(expectedSum, row.Values.Span[0].AsNumber());
        Assert.Equal(expectedCount, row.Values.Span[1].AsNumber());
    }

    [Fact]
    public async Task ExternalHeaderRow_AggregatesAsync_MatchLinqOverRangeRecordsOnly()
    {
        SalesRecord[] window = WindowRecords();
        double expectedSum = window.Sum(d => d.Units);
        int expectedCount = window.Length;

        using var ms = SalesWorkbook.Build(titleRow: true);
        using var workbook = ExcelWorkbook.Open(ms);

        QueryResult result = await workbook
            .QueryRange(SalesWorkbook.SheetName, Range, HeaderRow)
            .Select(QueryAggregates.Sum("Units"), QueryAggregates.Count())
            .ExecuteAsync(TestContext.Current.CancellationToken);

        var row = Assert.Single(result.Rows);
        Assert.Equal(expectedSum, row.Values.Span[0].AsNumber());
        Assert.Equal(expectedCount, row.Values.Span[1].AsNumber());
    }

    [Fact]
    public void ExternalHeaderRow_DistinctValues_CountsOnlyRangeRows()
    {
        DistinctValueCount[] expected = [.. WindowRecords()
            .GroupBy(d => d.Region)
            .Select(g => new DistinctValueCount(g.Key, g.Count()))
            .OrderByDescending(v => v.Count)
            .ThenBy(v => v.Value, StringComparer.Ordinal)];

        using var ms = SalesWorkbook.Build(titleRow: true);
        using var workbook = ExcelWorkbook.Open(ms);

        IReadOnlyList<DistinctValueCount> values = workbook
            .QueryRange(SalesWorkbook.SheetName, Range, HeaderRow)
            .DistinctValues("Region");

        Assert.Equal(expected, values);
    }

    [Fact]
    public async Task ExternalHeaderRow_DistinctValuesAsync_MatchesSync()
    {
        DistinctValueCount[] expected = [.. WindowRecords()
            .GroupBy(d => d.Region)
            .Select(g => new DistinctValueCount(g.Key, g.Count()))
            .OrderByDescending(v => v.Count)
            .ThenBy(v => v.Value, StringComparer.Ordinal)];

        using var ms = SalesWorkbook.Build(titleRow: true);
        using var workbook = ExcelWorkbook.Open(ms);

        IReadOnlyList<DistinctValueCount> values = await workbook
            .QueryRange(SalesWorkbook.SheetName, Range, HeaderRow)
            .DistinctValuesAsync("Region", ct: TestContext.Current.CancellationToken);

        Assert.Equal(expected, values);
    }

    [Fact]
    public void ExternalHeaderRow_WhereFilter_ResolvesColumnAndMatchesWithinRange()
    {
        int[] expectedRows = [.. SalesWorkbook.Data
            .Select((d, i) => (d, Row: SalesWorkbook.SheetRowOf(i, HeaderRow)))
            .Where(x => x.Row >= RangeTopRow && x.Row <= RangeBottomRow && x.d.Region == "AMER")
            .Select(x => x.Row)];

        using var ms = SalesWorkbook.Build(titleRow: true);
        using var workbook = ExcelWorkbook.Open(ms);

        QueryResult result = workbook
            .QueryRange(SalesWorkbook.SheetName, Range, HeaderRow)
            .Where("Region", QueryOperator.Equals, "AMER")
            .Execute();

        Assert.Equal(expectedRows, result.Rows.Select(r => r.SourceRowIndex!.Value));
        Assert.All(result.Rows, r => Assert.Equal("AMER", r.Values.Span[0].AsText()));
    }

    [Fact]
    public void ExecuteQuery_ExternalHeaderRow_SelectAll_ReturnsOnlyRangeRows()
    {
        int[] expectedRows = WindowRowIndices();

        using var ms = SalesWorkbook.Build(titleRow: true);
        using var workbook = ExcelWorkbook.Open(ms);

        QueryResult result = workbook.ExecuteQuery("""
            FROM Sales!A6:F12 HEADER ROW 2
            SELECT *
            """);

        Assert.Equal(SalesWorkbook.Headers, result.Columns);
        Assert.Equal(expectedRows, result.Rows.Select(r => r.SourceRowIndex!.Value));
    }

    [Fact]
    public async Task ExecuteQueryAsync_ExternalHeaderRow_SelectAll_ReturnsOnlyRangeRows()
    {
        int[] expectedRows = WindowRowIndices();

        using var ms = SalesWorkbook.Build(titleRow: true);
        using var workbook = ExcelWorkbook.Open(ms);

        QueryResult result = await workbook.ExecuteQueryAsync(
            """
            FROM Sales!A6:F12 HEADER ROW 2
            SELECT *
            """,
            TestContext.Current.CancellationToken);

        Assert.Equal(SalesWorkbook.Headers, result.Columns);
        Assert.Equal(expectedRows, result.Rows.Select(r => r.SourceRowIndex!.Value));
    }

    [Fact]
    public void ExecuteQuery_ExternalHeaderRow_Aggregate_MatchesWindowRecords()
    {
        SalesRecord[] window = WindowRecords();
        double expectedSum = window.Sum(d => d.Units);
        int expectedCount = window.Length;

        using var ms = SalesWorkbook.Build(titleRow: true);
        using var workbook = ExcelWorkbook.Open(ms);

        QueryResult result = workbook.ExecuteQuery("""
            FROM Sales!A6:F12 HEADER ROW 2
            SELECT SUM(Units), COUNT()
            """);

        var row = Assert.Single(result.Rows);
        Assert.Equal(expectedSum, row.Values.Span[0].AsNumber());
        Assert.Equal(expectedCount, row.Values.Span[1].AsNumber());
    }

    [Fact]
    public void HeaderRow_ExactlyAtRangeTop_StillBindsAndScansAsBefore()
    {
        // Regression guard: header row inside (at the top of) the range is today's behaviour
        // and must remain unchanged by the external-header feature. This test should PASS today.
        using var ms = SalesWorkbook.Build(titleRow: true);
        using var workbook = ExcelWorkbook.Open(ms);

        QueryResult result = workbook
            .QueryRange(SalesWorkbook.SheetName, "A2:F12", headerRow: 2)
            .Select(QueryAggregates.Count())
            .Execute();

        Assert.Equal(SalesWorkbook.Data.Length, result.RowsScanned);
        Assert.Equal(SalesWorkbook.Data.Length, result.Rows[0].Values.Span[0].AsNumber());
    }

    [Fact]
    public void HeaderRow_BelowRangeBottom_ThrowsArgumentOutOfRange()
    {
        using var ms = SalesWorkbook.Build(titleRow: true);
        using var workbook = ExcelWorkbook.Open(ms);

        Assert.Throws<ArgumentOutOfRangeException>(
            () => workbook.QueryRange(SalesWorkbook.SheetName, Range, headerRow: RangeBottomRow + 1));
    }

    [Fact]
    public void ExternalHeaderRow_EmptyHeaderRow_ThrowsMentioningHeaderRow()
    {
        // "Data" sheet has no cells at all on row 2 (the row is absent from sheetData), and its
        // first real content starts at row 6 — well below the queried range top. Binding the
        // external header at row 2 must fail the same way the in-range empty-header case does.
        using var ms = BuildMinimalWorkbook("Data", EmptyHeaderSheetXml, EmptyHeaderSstXml);
        using var workbook = ExcelWorkbook.Open(ms);

        var ex = Assert.Throws<InvalidOperationException>(() =>
            workbook.QueryRange("Data", "A6:B7", headerRow: 2).Execute());

        Assert.Contains("Header row 2", ex.Message, StringComparison.Ordinal);
        Assert.Contains("no cells", ex.Message, StringComparison.Ordinal);
    }

    // ── Minimal fixture for the empty-header-row test ──────────────────────────
    // Row 2 (the intended header row) is entirely absent from sheetData. Rows 6-7 hold two
    // data-shaped rows below the queried range top, so header binding must fail before any
    // data row is ever read.
    // SST: 0=EMEA, 1=APAC
    private const string EmptyHeaderSheetXml = """
        <worksheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main">
          <sheetData>
            <row r="6"><c r="A6" t="s"><v>0</v></c><c r="B6"><v>10</v></c></row>
            <row r="7"><c r="A7" t="s"><v>1</v></c><c r="B7"><v>20</v></c></row>
          </sheetData>
        </worksheet>
        """;

    private const string EmptyHeaderSstXml = """
        <sst xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main" uniqueCount="2">
          <si><t>EMEA</t></si>
          <si><t>APAC</t></si>
        </sst>
        """;

    private static MemoryStream BuildMinimalWorkbook(string sheetName, string sheetXml, string sstXml)
    {
        string workbookXml = $"""
            <workbook xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main"
                      xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships">
              <sheets>
                <sheet name="{sheetName}" sheetId="1" r:id="rId1" />
              </sheets>
            </workbook>
            """;

        const string relsXml = """
            <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
              <Relationship Id="rId1" Target="worksheets/sheet1.xml" />
            </Relationships>
            """;

        const string stylesXml = """
            <styleSheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main">
              <cellXfs><xf numFmtId="0" /></cellXfs>
            </styleSheet>
            """;

        var ms = new MemoryStream();
        using (var archive = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
        {
            WriteZipEntry(archive, "xl/workbook.xml", workbookXml);
            WriteZipEntry(archive, "xl/_rels/workbook.xml.rels", relsXml);
            WriteZipEntry(archive, "xl/styles.xml", stylesXml);
            WriteZipEntry(archive, "xl/sharedStrings.xml", sstXml);
            WriteZipEntry(archive, "xl/worksheets/sheet1.xml", sheetXml);
        }

        ms.Position = 0;
        return ms;
    }

    private static void WriteZipEntry(ZipArchive archive, string path, string content)
    {
        var entry = archive.CreateEntry(path);
        using var stream = entry.Open();
        using var writer = new StreamWriter(stream, Encoding.UTF8, leaveOpen: true);
        writer.Write(content);
    }
}
