using System.IO.Compression;
using System.Text;
using Xunit;

namespace XLSight.Query.Tests;

public sealed class QueryDslTests
{
    private const string Range = "A1:F11";

    // ── HeaderAuto test fixtures ──────────────────────────────────────────────
    // Row 1: sparse title "Q1 Report" in A1. Rows 2-3: empty gap (forces the title block to seal).
    // Row 4: header (Region | Units | Amount). Rows 5-7: data (1 text label + 2 numerics each).
    // Body numeric ratio = 2/3 > 0.5, so the analyzer classifies rows 4-7 as DataTable.
    // SST: 0=Q1 Report, 1=Region, 2=Units, 3=Amount, 4=EMEA, 5=APAC
    private const string HeaderAutoSheetXml = """
        <worksheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main">
          <sheetData>
            <row r="1"><c r="A1" t="s"><v>0</v></c></row>
            <row r="4">
              <c r="A4" t="s"><v>1</v></c>
              <c r="B4" t="s"><v>2</v></c>
              <c r="C4" t="s"><v>3</v></c>
            </row>
            <row r="5"><c r="A5" t="s"><v>4</v></c><c r="B5"><v>10</v></c><c r="C5"><v>100</v></c></row>
            <row r="6"><c r="A6" t="s"><v>5</v></c><c r="B6"><v>20</v></c><c r="C6"><v>200</v></c></row>
            <row r="7"><c r="A7" t="s"><v>4</v></c><c r="B7"><v>30</v></c><c r="C7"><v>300</v></c></row>
          </sheetData>
        </worksheet>
        """;

    private const string HeaderAutoSstXml = """
        <sst xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main" uniqueCount="6">
          <si><t>Q1 Report</t></si>
          <si><t>Region</t></si>
          <si><t>Units</t></si>
          <si><t>Amount</t></si>
          <si><t>EMEA</t></si>
          <si><t>APAC</t></si>
        </sst>
        """;

    // ── Two stacked tables for HeaderAuto sub-table targeting ─────────────────
    // Table 1 (rows 1-4): header "Region | Units" at row 1, data rows 2-4.
    // Gap: rows 5-6 blank (seals table 1).
    // Table 2 (rows 7-10): header "Product | Revenue" at row 7, data rows 8-10.
    // Observed: two DataTable regions with HeaderRows=[1] and HeaderRows=[7].
    // SST: 0=Region, 1=Units, 2=EMEA, 3=APAC, 4=Product, 5=Revenue, 6=Widget, 7=Gadget
    private const string TwoTableSheetXml = """
        <worksheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main">
          <sheetData>
            <row r="1"><c r="A1" t="s"><v>0</v></c><c r="B1" t="s"><v>1</v></c></row>
            <row r="2"><c r="A2" t="s"><v>2</v></c><c r="B2"><v>10</v></c></row>
            <row r="3"><c r="A3" t="s"><v>3</v></c><c r="B3"><v>20</v></c></row>
            <row r="4"><c r="A4" t="s"><v>2</v></c><c r="B4"><v>30</v></c></row>
            <row r="7"><c r="A7" t="s"><v>4</v></c><c r="B7" t="s"><v>5</v></c></row>
            <row r="8"><c r="A8" t="s"><v>6</v></c><c r="B8"><v>100</v></c></row>
            <row r="9"><c r="A9" t="s"><v>7</v></c><c r="B9"><v>200</v></c></row>
            <row r="10"><c r="A10" t="s"><v>6</v></c><c r="B10"><v>300</v></c></row>
          </sheetData>
        </worksheet>
        """;

    private const string TwoTableSstXml = """
        <sst xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main" uniqueCount="8">
          <si><t>Region</t></si>
          <si><t>Units</t></si>
          <si><t>EMEA</t></si>
          <si><t>APAC</t></si>
          <si><t>Product</t></si>
          <si><t>Revenue</t></si>
          <si><t>Widget</t></si>
          <si><t>Gadget</t></si>
        </sst>
        """;

    // ── FootnoteMarker test fixtures ──────────────────────────────────────────
    // Row 1: header Region | Units* (asterisk marker). Rows 2-4: data.
    // SST: 0=Region, 1=Units*, 2=EMEA, 3=APAC
    private const string FootnoteSheetXml = """
        <worksheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main">
          <sheetData>
            <row r="1"><c r="A1" t="s"><v>0</v></c><c r="B1" t="s"><v>1</v></c></row>
            <row r="2"><c r="A2" t="s"><v>2</v></c><c r="B2"><v>10</v></c></row>
            <row r="3"><c r="A3" t="s"><v>3</v></c><c r="B3"><v>5</v></c></row>
            <row r="4"><c r="A4" t="s"><v>2</v></c><c r="B4"><v>20</v></c></row>
          </sheetData>
        </worksheet>
        """;

    private const string FootnoteSstXml = """
        <sst xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main" uniqueCount="4">
          <si><t>Region</t></si>
          <si><t>Units*</t></si>
          <si><t>EMEA</t></si>
          <si><t>APAC</t></si>
        </sst>
        """;

    [Fact]
    public void Constructor_WithInnerException_UsesUnknownPosition()
    {
        var exception = new QueryDslException("message", new InvalidOperationException());

        Assert.Equal(-1, exception.Position);
    }

    [Fact]
    public void Parse_ValidAggregateQuery_ReturnsStructuredSpec()
    {
        SheetQuerySpec spec = SheetQuerySpec.Parse("""
            FROM Sales!A1:F11 HEADER AUTO
            SELECT SUM(Units), COUNT()
            WHERE Region = "EMEA" AND Units > 1
            GROUP BY Month
            LIMIT 5
            """);

        Assert.Equal(SalesWorkbook.SheetName, spec.Sheet);
        Assert.Equal(Range, spec.RangeAddress);
        Assert.Equal(SheetQueryHeaderKind.Auto, spec.Header.Kind);
        Assert.False(spec.SelectAll);
        Assert.Equal(["Sum(Units)", "Count()"], spec.Aggregates.Select(a => a.Label));
        Assert.Equal(2, spec.Predicates.Count);
        Assert.Equal("Month", spec.GroupBy);
        Assert.Equal(5, spec.Limit);
    }

    [Fact]
    public void Parse_QuotedIdentifiersAndEscapedText_PreservesValues()
    {
        SheetQuerySpec spec = SheetQuerySpec.Parse(
            "FROM \"Sales\"!A1:F11 HEADER AUTO\n" +
            "SELECT COUNT()\n" +
            "WHERE \"Region\" = \"Revenue \"\"Actual\"\"\"");

        SheetQueryPredicate predicate = Assert.Single(spec.Predicates);
        Assert.Equal("Region", predicate.Column);
        Assert.Equal("Revenue \"Actual\"", predicate.Literal.AsText());
    }

    [Fact]
    public void Parse_BareIdentifiersCanStartWithDigits()
    {
        SheetQuerySpec spec = SheetQuerySpec.Parse("""
            FROM 2025_Sales!A1:F11 HEADER AUTO
            SELECT SUM(2025)
            GROUP BY Q1
            """);

        Assert.Equal("2025_Sales", spec.Sheet);
        Assert.Equal("Sum(2025)", Assert.Single(spec.Aggregates).Label);
        Assert.Equal("Q1", spec.GroupBy);
    }

    [Fact]
    public void ExecuteQuery_SelectAll_ReturnsFilteredRows()
    {
        int[] expectedRows = [.. SalesWorkbook.Data
            .Select((d, i) => (d, Row: SalesWorkbook.SheetRowOf(i)))
            .Where(x => string.Equals(x.d.Region, "APAC", StringComparison.Ordinal) && x.d.Units > 3)
            .Select(x => x.Row)];

        using var ms = SalesWorkbook.Build();
        using var workbook = ExcelWorkbook.Open(ms);

        QueryResult result = workbook.ExecuteQuery("""
            FROM Sales!A1:F11 HEADER AUTO
            SELECT *
            WHERE Region = "APAC" AND Units > 3
            LIMIT 10
            """);

        Assert.Equal(SalesWorkbook.Headers, result.Columns);
        Assert.Equal(expectedRows, result.Rows.Select(r => r.SourceRowIndex!.Value));
    }

    [Fact]
    public void ExecuteQuery_AggregatesWithGroupBy_MatchesFluentQuery()
    {
        using var ms = SalesWorkbook.Build();
        using var workbook = ExcelWorkbook.Open(ms);

        QueryResult expected = workbook
            .QueryRange(SalesWorkbook.SheetName, Range)
            .Where("Region", QueryOperator.Equals, "EMEA")
            .Where("Units", QueryOperator.GreaterThan, 1)
            .GroupBy("Month")
            .Select(QueryAggregates.Sum("Units"), QueryAggregates.Count())
            .Take(2)
            .Execute();

        QueryResult actual = workbook.ExecuteQuery("""
            FROM Sales!A1:F11 HEADER AUTO
            SELECT SUM(Units), COUNT()
            WHERE Region = "EMEA" AND Units > 1
            GROUP BY Month
            LIMIT 2
            """);

        Assert.Equal(expected.Columns, actual.Columns);
        Assert.Equal(
            expected.Rows.Select(r => string.Join("|", r.Values.ToArray())),
            actual.Rows.Select(r => string.Join("|", r.Values.ToArray())));
        Assert.Equal(expected.RowsScanned, actual.RowsScanned);
        Assert.Equal(expected.RowsMatched, actual.RowsMatched);
    }

    [Fact]
    public void ExecuteQuery_HeaderRow_UsesExplicitHeader()
    {
        using var ms = SalesWorkbook.Build(titleRow: true);
        using var workbook = ExcelWorkbook.Open(ms);

        QueryResult result = workbook.ExecuteQuery("""
            FROM Sales!A1:F12 HEADER ROW 2
            SELECT COUNT()
            WHERE Region = "EMEA"
            """);

        Assert.Equal(
            SalesWorkbook.Data.Count(d => string.Equals(d.Region, "EMEA", StringComparison.Ordinal)),
            result.Rows[0].Values.Span[0].AsNumber());
    }

    [Fact]
    public void ExecuteQuery_DateAndBooleanPredicates_MatchReference()
    {
        using var ms = SalesWorkbook.Build();
        using var workbook = ExcelWorkbook.Open(ms);

        QueryResult result = workbook.ExecuteQuery("""
            FROM Sales!A1:F11 HEADER AUTO
            SELECT COUNT()
            WHERE OrderDate >= DATE "2024-03-01" AND OnPromo = TRUE
            """);

        Assert.Equal(
            SalesWorkbook.Data.Count(d => d.OrderDate >= new DateTime(2024, 3, 1) && d.OnPromo),
            result.Rows[0].Values.Span[0].AsNumber());
    }

    [Fact]
    public async Task ExecuteQueryAsync_ProducesSameResultAsSync()
    {
        using var ms = SalesWorkbook.Build();
        using var workbook = ExcelWorkbook.Open(ms);

        const string query = """
            FROM Sales!A1:F11 HEADER AUTO
            SELECT AVG(Units), MIN(OrderDate), MAX(OrderDate)
            WHERE Region != "APAC"
            """;

        QueryResult sync = workbook.ExecuteQuery(query);
        QueryResult async = await workbook.ExecuteQueryAsync(query, TestContext.Current.CancellationToken);

        Assert.Equal(sync.Columns, async.Columns);
        Assert.Equal(
            sync.Rows.Select(r => string.Join("|", r.Values.ToArray())),
            async.Rows.Select(r => string.Join("|", r.Values.ToArray())));
    }

    [Fact]
    public void ExecuteQuery_HeaderColumn_ParsesButDoesNotExecute()
    {
        SheetQuerySpec spec = SheetQuerySpec.Parse("""
            FROM Sales!A1:F11 HEADER COLUMN A
            SELECT COUNT()
            """);

        using var ms = SalesWorkbook.Build();
        using var workbook = ExcelWorkbook.Open(ms);

        var ex = Assert.Throws<NotSupportedException>(() => workbook.ExecuteQuery(spec));
        Assert.Contains("HEADER COLUMN", ex.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("A1:F11", "SELECT Region", "Projected row columns are not supported")]
    [InlineData("A1:F11", "SELECT TOTAL(Units)", "Unknown aggregate 'TOTAL'")]
    [InlineData("A1:F11", "SELECT * GROUP BY Region", "GROUP BY is not valid with SELECT *")]
    [InlineData("A1:F11", "SELECT COUNT() WHERE Region = \"EMEA\" OR Region = \"APAC\"", "OR is not supported")]
    [InlineData("A1:F11", "SELECT COUNT() WHERE OnPromo > true", "Boolean predicates support '=' and '!=' only")]
    [InlineData("A:F", "SELECT COUNT()", "FROM range must be a bounded A1 range")]
    public void Parse_UnsupportedSyntax_ThrowsRepairableDiagnostic(
        string range,
        string clauses,
        string expectedMessage)
    {
        string query = $"FROM Sales!{range} HEADER AUTO\n{clauses}";

        var ex = Assert.Throws<QueryDslException>(() => SheetQuerySpec.Parse(query));
        Assert.Contains(expectedMessage, ex.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// Sheet layout: A1 = sparse title, rows 2-3 = empty gap (forces region seal),
    /// row 4 = header (Region | Units | Amount), rows 5-7 = data (1 text + 2 numerics each).
    /// HEADER AUTO over A1:C7 must bind row 4 as the header — not row 1 (the sheet title).
    /// </summary>
    [Fact]
    public void HeaderAuto_ResolvesAnalyzerHeaderRow_NotSheetTitle()
    {
        using var ms = BuildMinimalWorkbook("Sheet1", HeaderAutoSheetXml, HeaderAutoSstXml);
        using var workbook = ExcelWorkbook.Open(ms);

        // The query range covers all rows including the title.
        QueryResult result = workbook.ExecuteQuery("""
            FROM Sheet1!A1:C7 HEADER AUTO
            SELECT SUM(Units)
            WHERE Region = "EMEA"
            """);

        // EMEA appears in rows 5 and 7: Units 10 + 30 = 40.
        // If row 1 were wrongly bound as the header, "Region" would not resolve and the query would throw.
        Assert.Equal(40, result.Rows[0].Values.Span[0].AsNumber());
    }

    /// <summary>
    /// A header cell "Units*" contains a trailing footnote marker.
    /// A filter referencing "Units" (without the marker) must match it.
    /// </summary>
    [Fact]
    public void HeaderWithFootnoteMarker_MatchesUnmarkedColumnName()
    {
        using var ms = BuildMinimalWorkbook("Sheet1", FootnoteSheetXml, FootnoteSstXml);
        using var workbook = ExcelWorkbook.Open(ms);

        // Filter and aggregate by "Units" — must resolve despite header being "Units*".
        QueryResult result = workbook.ExecuteQuery("""
            FROM Sheet1!A1:B4 HEADER ROW 1
            SELECT SUM(Units)
            WHERE Region = "EMEA"
            """);

        // EMEA rows: row 2 (Units=10) and row 4 (Units=20) → SUM = 30.
        Assert.Equal(30, result.Rows[0].Values.Span[0].AsNumber());
    }

    /// <summary>
    /// Sheet has two stacked DataTable regions. A HEADER AUTO query whose range is bounded to
    /// the SECOND table (A7:B10) must bind row 7 as the header — not row 1 (the first table's header).
    /// This guards the region-intersection logic in ResolveAutoHeaderRow.
    /// </summary>
    [Fact]
    public void HeaderAuto_BindsHeaderOfTargetedSubTable()
    {
        using var ms = BuildMinimalWorkbook("Data", TwoTableSheetXml, TwoTableSstXml);
        using var workbook = ExcelWorkbook.Open(ms);

        // Range A7:B10 covers only table 2 (header "Product | Revenue", data rows 8-10).
        // If HEADER AUTO incorrectly bound row 1 ("Region" and "Units"), "Revenue" would not resolve.
        QueryResult result = workbook.ExecuteQuery("""
            FROM Data!A7:B10 HEADER AUTO
            SELECT SUM(Revenue)
            WHERE Product = "Widget"
            """);

        // Widget appears in rows 8 (100) and 10 (300); SUM = 400.
        Assert.Equal(400, result.Rows[0].Values.Span[0].AsNumber());
    }

    [Fact]
    public void HeaderAuto_RegionHeaderAboveSubRange_FallsBackWithoutThrowing()
    {
        using var ms = BuildMinimalWorkbook("Data", TwoTableSheetXml, TwoTableSstXml);
        using var workbook = ExcelWorkbook.Open(ms);

        // A8:B10 covers table 2's data rows only; its header (row 7) sits above the range.
        // HEADER AUTO must not return that out-of-range header (QueryRange would throw) — it
        // falls back to first-non-empty-row binding within the range instead.
        Exception? ex = Record.Exception(() =>
            workbook.ExecuteQuery("FROM Data!A8:B10 HEADER AUTO SELECT *"));

        Assert.Null(ex);
    }

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
