using System.Globalization;
using System.IO.Compression;
using System.Text;
using Xunit;

namespace XLSight.Query.Tests;

/// <summary>
/// Covers <c>ORDER BY</c> on raw-row results (<c>SELECT *</c> or a raw column projection): a
/// bounded top-N selection ranks every matching row against the ordering key and keeps the best
/// <c>LIMIT</c> rows, trading away the row-mode early exit in exchange for a correct ranking.
/// </summary>
public sealed class QueryRowOrderByTests
{
    private const string Range = "A1:F11";

    [Fact]
    public void RowOrderBy_NetSalesDescendingLimitThree_ReturnsTopThreeRows()
    {
        using var ms = SalesWorkbook.Build();
        using var workbook = ExcelWorkbook.Open(ms);

        QueryResult result = workbook.ExecuteQuery("""
            FROM Sales!A1:F11 HEADER AUTO
            SELECT *
            ORDER BY NetSales DESC
            LIMIT 3
            """);

        Assert.Equal(SalesWorkbook.Headers, result.Columns);
        Assert.Equal(["300", "200.25", "100.5"], result.Rows.Select(r => r.Values.Span[2].ToString()));
    }

    [Fact]
    public void RowOrderBy_UnitsAscendingLimitTwo_ReturnsSmallestTwo()
    {
        using var ms = SalesWorkbook.Build();
        using var workbook = ExcelWorkbook.Open(ms);

        QueryResult result = workbook.ExecuteQuery("""
            FROM Sales!A1:F11 HEADER AUTO
            SELECT *
            ORDER BY Units ASC
            LIMIT 2
            """);

        Assert.Equal([1d, 2d], result.Rows.Select(r => r.Values.Span[3].AsNumber()));
    }

    [Fact]
    public void RowOrderBy_OrderColumnNotSelected_StillOrders()
    {
        using var ms = SalesWorkbook.Build();
        using var workbook = ExcelWorkbook.Open(ms);

        QueryResult result = workbook.ExecuteQuery("""
            FROM Sales!A1:F11 HEADER AUTO
            SELECT Region
            ORDER BY Units DESC
            LIMIT 2
            """);

        Assert.Equal(["Region"], result.Columns);
        Assert.Equal(["AMER", "EMEA"], result.Rows.Select(r => r.Values.Span[0].ToString()));
    }

    [Fact]
    public void RowOrderBy_LimitExceedsMatchCount_ReturnsAllRowsSorted()
    {
        using var ms = SalesWorkbook.Build();
        using var workbook = ExcelWorkbook.Open(ms);

        QueryResult result = workbook.ExecuteQuery("""
            FROM Sales!A1:F11 HEADER AUTO
            SELECT *
            ORDER BY NetSales DESC
            LIMIT 100
            """);

        Assert.Equal(SalesWorkbook.Data.Length, result.Rows.Count);
        Assert.Equal(
            ["300", "200.25", "100.5", "75", "60", "50", "25.75", "10", "n/a", "Empty"],
            result.Rows.Select(r => r.Values.Span[2].ToString()));
    }

    [Fact]
    public void RowOrderBy_TextAndEmptyCells_EmptyStillLastUnderAscending()
    {
        using var ms = SalesWorkbook.Build();
        using var workbook = ExcelWorkbook.Open(ms);

        QueryResult result = workbook.ExecuteQuery("""
            FROM Sales!A1:F11 HEADER AUTO
            SELECT *
            ORDER BY NetSales ASC
            LIMIT 10
            """);

        // Empty is never "largest" merely because direction flipped — it stays last under ASC too.
        Assert.Equal("Empty", result.Rows[^1].Values.Span[2].ToString());
    }

    [Fact]
    public void RowOrderBy_EqualKeys_KeepSheetOrder()
    {
        using var ms = SalesWorkbook.Build();
        using var workbook = ExcelWorkbook.Open(ms);

        QueryResult result = workbook.ExecuteQuery("""
            FROM Sales!A1:F11 HEADER AUTO
            SELECT *
            ORDER BY Region ASC
            LIMIT 3
            """);

        // The three AMER rows tie on the ordering key; without a tiebreak an unstable selection
        // could reorder them. Sheet order must be preserved.
        Assert.All(result.Rows, r => Assert.Equal("AMER", r.Values.Span[0].ToString()));
        Assert.Equal([6, 8, 11], result.Rows.Select(r => r.SourceRowIndex!.Value));
    }

    [Fact]
    public void RowOrderBy_WithoutLimit_Rejected()
    {
        var ex = Assert.Throws<QueryDslException>(() => SheetQuerySpec.Parse($"""
            FROM Sales!{Range} HEADER AUTO
            SELECT *
            ORDER BY NetSales DESC
            """));

        Assert.Equal(
            "ORDER BY requires LIMIT on row results. Add LIMIT n, or GROUP BY to rank aggregated groups.",
            ex.Message);
    }

    [Fact]
    public void RowOrderBy_ScansEveryRow_UnlikeUnorderedLimit()
    {
        using var ms = SalesWorkbook.Build();
        using var workbook = ExcelWorkbook.Open(ms);

        QueryResult ordered = workbook.ExecuteQuery("""
            FROM Sales!A1:F11 HEADER AUTO
            SELECT *
            ORDER BY NetSales DESC
            LIMIT 3
            """);

        QueryResult unordered = workbook.ExecuteQuery("""
            FROM Sales!A1:F11 HEADER AUTO
            SELECT *
            LIMIT 3
            """);

        // Proof that ORDER BY trades away the row-mode early exit: every row must be seen to
        // find the true top-N, while an unordered LIMIT stops as soon as it has enough rows.
        Assert.Equal(SalesWorkbook.Data.Length, ordered.RowsScanned);
        Assert.Equal(3, unordered.RowsScanned);
    }

    [Fact]
    public void RowOrderBy_WithWhereFilter_RanksOnlyMatchingRows()
    {
        using var ms = SalesWorkbook.Build();
        using var workbook = ExcelWorkbook.Open(ms);

        QueryResult result = workbook.ExecuteQuery("""
            FROM Sales!A1:F11 HEADER AUTO
            SELECT *
            WHERE Region = "EMEA"
            ORDER BY NetSales DESC
            LIMIT 10
            """);

        Assert.Equal(4, result.Rows.Count);
        Assert.All(result.Rows, r => Assert.Equal("EMEA", r.Values.Span[0].ToString()));
        Assert.Equal(
            ["200.25", "100.5", "10", "Empty"],
            result.Rows.Select(r => r.Values.Span[2].ToString()));
    }

    [Fact]
    public void RowOrderBy_FluentApi_MatchesDslResult()
    {
        using var ms = SalesWorkbook.Build();
        using var workbook = ExcelWorkbook.Open(ms);

        QueryResult fluent = workbook
            .QueryRange(SalesWorkbook.SheetName, Range)
            .Project("Region", "NetSales")
            .OrderBy("NetSales", descending: true)
            .Take(3)
            .Execute();

        QueryResult dsl = workbook.ExecuteQuery("""
            FROM Sales!A1:F11 HEADER AUTO
            SELECT Region, NetSales
            ORDER BY NetSales DESC
            LIMIT 3
            """);

        Assert.Equal(dsl.Columns, fluent.Columns);
        Assert.Equal(
            dsl.Rows.Select(r => string.Join("|", r.Values.ToArray())),
            fluent.Rows.Select(r => string.Join("|", r.Values.ToArray())));
    }

    [Fact]
    public void RowOrderBy_SurvivorsExceedInitialArena_GrowsAndOrdersAll()
    {
        // 200 survivors force the top-N arena past its initial capacity several times. Growth
        // must not disturb the slots already held by the heap.
        using var ms = BuildNumberWorkbook(rowCount: 300);
        using var workbook = ExcelWorkbook.Open(ms);

        QueryResult result = workbook.ExecuteQuery("""
            FROM Sheet1!A1:A301 HEADER ROW 1
            SELECT *
            ORDER BY Key DESC
            LIMIT 200
            """);

        Assert.Equal(200, result.Rows.Count);
        Assert.Equal(
            Enumerable.Range(101, 200).Reverse().Select(i => (double)i),
            result.Rows.Select(r => r.Values.Span[0].AsNumber()));
    }

    [Fact]
    public void RowOrderBy_LimitFarExceedsMatches_AllocatesForMatchesOnly()
    {
        // A LIMIT this large would once have sized the arena from the requested count, either
        // allocating gigabytes or overflowing the arena length outright.
        using var ms = SalesWorkbook.Build();
        using var workbook = ExcelWorkbook.Open(ms);

        QueryResult result = workbook.ExecuteQuery($"""
            FROM Sales!A1:F11 HEADER AUTO
            SELECT *
            ORDER BY NetSales DESC
            LIMIT {int.MaxValue}
            """);

        Assert.Equal(SalesWorkbook.Data.Length, result.Rows.Count);
        Assert.Equal("300", result.Rows[0].Values.Span[2].ToString());
    }

    [Fact]
    public void RowOrderBy_NumbersAndDatesInOneColumn_GroupNumbersBeforeDates()
    {
        using var ms = BuildMixedNumberDateWorkbook();
        using var workbook = ExcelWorkbook.Open(ms);

        QueryResult result = workbook.ExecuteQuery("""
            FROM Sheet1!A1:A5 HEADER ROW 1
            SELECT *
            ORDER BY Key ASC
            LIMIT 10
            """);

        // A serial number and a date are not commensurable, so all numbers rank ahead of all
        // dates rather than interleaving by tick value.
        Assert.Equal(
            [CellType.Number, CellType.Number, CellType.Date, CellType.Date],
            result.Rows.Select(r => r.Values.Span[0].CellType));
        Assert.Equal([5d, 100d], result.Rows.Take(2).Select(r => r.Values.Span[0].AsNumber()));
        Assert.Equal(
            [new DateTime(2024, 1, 15), new DateTime(2024, 3, 5)],
            result.Rows.Skip(2).Select(r => r.Values.Span[0].AsDate()));
    }

    /// <summary>A single numeric "Key" column holding <paramref name="rowCount"/> descending values.</summary>
    private static MemoryStream BuildNumberWorkbook(int rowCount)
    {
        var sb = new StringBuilder();
        sb.Append("""<worksheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main"><sheetData>""");
        sb.Append("""<row r="1"><c r="A1" t="s"><v>0</v></c></row>""");
        for (int i = 0; i < rowCount; i++)
        {
            sb.Append(CultureInfo.InvariantCulture, $"""<row r="{i + 2}"><c r="A{i + 2}"><v>{rowCount - i}</v></c></row>""");
        }

        sb.Append("</sheetData></worksheet>");
        return BuildWorkbook(sb.ToString());
    }

    /// <summary>A "Key" column mixing plain numbers with date-formatted cells.</summary>
    private static MemoryStream BuildMixedNumberDateWorkbook()
    {
        double marchSerial = (new DateTime(2024, 3, 5) - new DateTime(1899, 12, 30)).TotalDays;
        double januarySerial = (new DateTime(2024, 1, 15) - new DateTime(1899, 12, 30)).TotalDays;

        string sheetXml = string.Create(CultureInfo.InvariantCulture, $"""
            <worksheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main">
              <sheetData>
                <row r="1"><c r="A1" t="s"><v>0</v></c></row>
                <row r="2"><c r="A2"><v>100</v></c></row>
                <row r="3"><c r="A3" s="1"><v>{marchSerial}</v></c></row>
                <row r="4"><c r="A4"><v>5</v></c></row>
                <row r="5"><c r="A5" s="1"><v>{januarySerial}</v></c></row>
              </sheetData>
            </worksheet>
            """);

        return BuildWorkbook(sheetXml);
    }

    private static MemoryStream BuildWorkbook(string sheetXml)
    {
        const string workbookXml = """
            <workbook xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main"
                      xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships">
              <sheets>
                <sheet name="Sheet1" sheetId="1" r:id="rId1" />
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
              <cellXfs>
                <xf numFmtId="0" />
                <xf numFmtId="14" applyNumberFormat="1" />
              </cellXfs>
            </styleSheet>
            """;

        const string sstXml = """
            <sst xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main" uniqueCount="1">
              <si><t>Key</t></si>
            </sst>
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
