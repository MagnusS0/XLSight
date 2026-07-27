using System.IO.Compression;
using System.Text;
using Xunit;

namespace XLSight.Query.Tests;

/// <summary>
/// Covers the "projected row columns" feature: <c>SELECT col[, col...]</c> returns raw row
/// results containing exactly those columns, in SELECT order. Filters may reference columns
/// that are not selected; mixing raw columns with aggregates, or with <c>GROUP BY</c>, is
/// rejected at parse time.
/// </summary>
public sealed class QueryProjectedColumnsTests
{
    private const string Range = "A1:F11";

    [Fact]
    public void DslProjection_TwoColumns_ReturnsExactlyThoseColumnsForAllDataRows()
    {
        using var ms = SalesWorkbook.Build();
        using var workbook = ExcelWorkbook.Open(ms);

        QueryResult result = workbook.ExecuteQuery("""
            FROM Sales!A1:F11 HEADER AUTO
            SELECT Region, NetSales
            """);

        Assert.Equal(["Region", "NetSales"], result.Columns);
        Assert.Equal(SalesWorkbook.Data.Length, result.Rows.Count);
        Assert.Equal(
            SalesWorkbook.Data.Select(d => $"{d.Region}|{ExpectedNetSalesCell(d)}"),
            result.Rows.Select(r => string.Join("|", r.Values.ToArray())));
    }

    [Fact]
    public void DslProjection_SelectOrderReversedFromSheetOrder_ColumnsFollowSelectOrder()
    {
        // Sheet header order is Region, Month, NetSales, ... — NetSales sits after Region,
        // so selecting "NetSales, Region" is the reverse of sheet order.
        using var ms = SalesWorkbook.Build();
        using var workbook = ExcelWorkbook.Open(ms);

        QueryResult result = workbook.ExecuteQuery("""
            FROM Sales!A1:F11 HEADER AUTO
            SELECT NetSales, Region
            """);

        Assert.Equal(["NetSales", "Region"], result.Columns);
        Assert.Equal(
            SalesWorkbook.Data.Select(d => $"{ExpectedNetSalesCell(d)}|{d.Region}"),
            result.Rows.Select(r => string.Join("|", r.Values.ToArray())));
    }

    [Fact]
    public void DslProjection_QuotedColumnNameWithSpace_ResolvesAndProjects()
    {
        using var ms = BuildQuotedHeaderWorkbook();
        using var workbook = ExcelWorkbook.Open(ms);

        QueryResult result = workbook.ExecuteQuery("""
            FROM Sheet1!A1:B3 HEADER ROW 1
            SELECT "Q3 2025"
            """);

        Assert.Equal(["Q3 2025"], result.Columns);
        Assert.Equal([100d, 200d], result.Rows.Select(r => r.Values.Span[0].AsNumber()));
    }

    [Fact]
    public void DslProjection_WhereOnUnselectedColumn_FiltersWithoutIncludingItInResult()
    {
        SalesRecord[] expected = [.. SalesWorkbook.Data.Where(d => string.Equals(d.Region, "APAC", StringComparison.Ordinal))];

        using var ms = SalesWorkbook.Build();
        using var workbook = ExcelWorkbook.Open(ms);

        QueryResult result = workbook.ExecuteQuery("""
            FROM Sales!A1:F11 HEADER AUTO
            SELECT NetSales
            WHERE Region = "APAC"
            """);

        Assert.Equal(["NetSales"], result.Columns);
        Assert.Equal(
            expected.Select(ExpectedNetSalesCell),
            result.Rows.Select(r => r.Values.Span[0].ToString()));
    }

    [Theory]
    [InlineData("SELECT Region, SUM(NetSales)")]
    [InlineData("SELECT SUM(NetSales), Region")]
    public void DslProjection_MixedWithAggregate_ThrowsQueryDslException(string selectClause)
    {
        string query = $"FROM Sales!{Range} HEADER AUTO\n{selectClause}";

        var ex = Assert.Throws<QueryDslException>(() => SheetQuerySpec.Parse(query));

        // The message must name the mixing rule specifically; the blanket "projected columns are
        // not supported" rejection this feature replaces would otherwise satisfy the assertion.
        Assert.Contains("mix", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void DslProjection_WithGroupBy_ThrowsQueryDslException()
    {
        string query = $"""
            FROM Sales!{Range} HEADER AUTO
            SELECT Region
            GROUP BY Month
            """;

        var ex = Assert.Throws<QueryDslException>(() => SheetQuerySpec.Parse(query));

        // Must fail on the grouping rule, not on the pre-feature blanket rejection of raw columns.
        Assert.Contains("GROUP BY", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void DslProjection_DuplicateColumn_YieldsTwoIdenticalResultColumns()
    {
        using var ms = SalesWorkbook.Build();
        using var workbook = ExcelWorkbook.Open(ms);

        QueryResult result = workbook.ExecuteQuery("""
            FROM Sales!A1:F11 HEADER AUTO
            SELECT Region, Region
            """);

        Assert.Equal(["Region", "Region"], result.Columns);
        Assert.All(result.Rows, r => Assert.Equal(r.Values.Span[0], r.Values.Span[1]));
    }

    [Fact]
    public void DslProjection_UnknownColumn_ThrowsListingAvailableColumns()
    {
        using var ms = SalesWorkbook.Build();
        using var workbook = ExcelWorkbook.Open(ms);

        var ex = Assert.Throws<InvalidOperationException>(() => workbook.ExecuteQuery("""
            FROM Sales!A1:F11 HEADER AUTO
            SELECT Regin
            """));

        Assert.Contains("Regin", ex.Message, StringComparison.Ordinal);
        Assert.Contains("Region", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void DslProjection_Limit_StopsScanAtNthRow()
    {
        using var ms = SalesWorkbook.Build();
        using var workbook = ExcelWorkbook.Open(ms);

        QueryResult result = workbook.ExecuteQuery("""
            FROM Sales!A1:F11 HEADER AUTO
            SELECT Region, NetSales
            LIMIT 3
            """);

        Assert.Equal(["Region", "NetSales"], result.Columns);
        Assert.Equal(3, result.Rows.Count);
        Assert.Equal(3, result.RowsScanned);
    }

    [Fact]
    public async Task DslProjection_ExecuteQueryAsync_AgreesWithSync()
    {
        using var ms = SalesWorkbook.Build();
        using var workbook = ExcelWorkbook.Open(ms);

        const string query = """
            FROM Sales!A1:F11 HEADER AUTO
            SELECT NetSales, Region
            """;

        QueryResult sync = workbook.ExecuteQuery(query);
        QueryResult async = await workbook.ExecuteQueryAsync(query, TestContext.Current.CancellationToken);

        // Anchored to the expected projection, not just sync-vs-async agreement: without this
        // the pair would agree while both returned every sheet column.
        Assert.Equal(["NetSales", "Region"], sync.Columns);
        Assert.Equal(sync.Columns, async.Columns);
        Assert.Equal(
            sync.Rows.Select(r => string.Join("|", r.Values.ToArray())),
            async.Rows.Select(r => string.Join("|", r.Values.ToArray())));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void FluentProject_BlankColumnName_ThrowsAtCallTime(string? column)
    {
        using var ms = SalesWorkbook.Build();
        using var workbook = ExcelWorkbook.Open(ms);

        SheetQuery query = workbook.QueryRange(SalesWorkbook.SheetName, Range);

        // Must fail on the offending argument, not survive to Execute() and resurface as a
        // "column not found" error listing the whole header. ThrowsAny because null yields
        // ArgumentNullException while blank yields ArgumentException.
        Assert.ThrowsAny<ArgumentException>(() => query.Project("Region", column!));
    }

    [Fact]
    public void FluentProject_MatchesDslProjection()
    {
        using var ms = SalesWorkbook.Build();
        using var workbook = ExcelWorkbook.Open(ms);

        QueryResult fluent = workbook
            .QueryRange(SalesWorkbook.SheetName, Range)
            .Project("Region", "NetSales")
            .Execute();

        QueryResult dsl = workbook.ExecuteQuery("""
            FROM Sales!A1:F11 HEADER AUTO
            SELECT Region, NetSales
            """);

        Assert.Equal(dsl.Columns, fluent.Columns);
        Assert.Equal(
            dsl.Rows.Select(r => string.Join("|", r.Values.ToArray())),
            fluent.Rows.Select(r => string.Join("|", r.Values.ToArray())));
    }

    [Fact]
    public void DslProjection_WithExternalHeader_ResolvesColumnsFromHeaderAboveRange()
    {
        // Mirrors QueryExternalHeaderTests' fixture: SalesWorkbook.Build(titleRow: true) puts the
        // banner in row 1, headers in row 2, and data starting at row 3. The queried range (rows
        // 6-12) sits below the header row, so only records whose sheet row falls in that window
        // are in scope.
        const int headerRow = 2;
        const int rangeTop = 6;
        const int rangeBottom = 12;

        SalesRecord[] window = [.. SalesWorkbook.Data
            .Where((d, i) =>
            {
                int row = SalesWorkbook.SheetRowOf(i, headerRow);
                return row >= rangeTop && row <= rangeBottom;
            })];

        using var ms = SalesWorkbook.Build(titleRow: true);
        using var workbook = ExcelWorkbook.Open(ms);

        QueryResult result = workbook.ExecuteQuery("""
            FROM Sales!A6:F12 HEADER ROW 2
            SELECT Region, NetSales
            """);

        Assert.Equal(["Region", "NetSales"], result.Columns);
        Assert.Equal(
            window.Select(d => $"{d.Region}|{ExpectedNetSalesCell(d)}"),
            result.Rows.Select(r => string.Join("|", r.Values.ToArray())));
    }

    [Fact]
    public void DslProjection_SelectStar_RegressionUnaffectedBySelectColumnList()
    {
        int[] expectedRows = [.. SalesWorkbook.Data
            .Select((d, i) => (d, Row: SalesWorkbook.SheetRowOf(i)))
            .Where(x => string.Equals(x.d.Region, "APAC", StringComparison.Ordinal))
            .Select(x => x.Row)];

        using var ms = SalesWorkbook.Build();
        using var workbook = ExcelWorkbook.Open(ms);

        QueryResult result = workbook.ExecuteQuery("""
            FROM Sales!A1:F11 HEADER AUTO
            SELECT *
            WHERE Region = "APAC"
            """);

        Assert.Equal(SalesWorkbook.Headers, result.Columns);
        Assert.Equal(expectedRows, result.Rows.Select(r => r.SourceRowIndex!.Value));
    }

    /// <summary>Renders a data record's NetSales cell the same way <see cref="ExcelCellValue.ToString"/> would.</summary>
    private static string ExpectedNetSalesCell(SalesRecord record)
    {
        if (record.NetSales is { } netSales)
        {
            return ExcelCellValue.FromNumber(netSales).ToString();
        }

        return record.NetSalesText is { } text ? text : "Empty";
    }

    private static MemoryStream BuildQuotedHeaderWorkbook()
    {
        // Row 1: header "Q3 2025" | "Region". Rows 2-3: two data rows.
        // SST: 0=Q3 2025, 1=Region, 2=EMEA, 3=APAC
        const string sheetXml = """
            <worksheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main">
              <sheetData>
                <row r="1"><c r="A1" t="s"><v>0</v></c><c r="B1" t="s"><v>1</v></c></row>
                <row r="2"><c r="A2"><v>100</v></c><c r="B2" t="s"><v>2</v></c></row>
                <row r="3"><c r="A3"><v>200</v></c><c r="B3" t="s"><v>3</v></c></row>
              </sheetData>
            </worksheet>
            """;

        const string sstXml = """
            <sst xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main" uniqueCount="4">
              <si><t>Q3 2025</t></si>
              <si><t>Region</t></si>
              <si><t>EMEA</t></si>
              <si><t>APAC</t></si>
            </sst>
            """;

        return BuildMinimalWorkbook("Sheet1", sheetXml, sstXml);
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
