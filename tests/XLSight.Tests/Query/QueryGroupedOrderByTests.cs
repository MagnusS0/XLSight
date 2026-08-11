using System.IO.Compression;
using System.Text;
using Xunit;

namespace XLSight.Query.Tests;

/// <summary>
/// Covers <c>ORDER BY</c> on grouped (<c>GROUP BY</c>) results: the materialized groups are
/// sorted on the group column or a selected aggregate before <c>LIMIT</c> truncates. Ordering
/// a raw-row result (<c>SELECT *</c> or a raw column projection) or a global aggregate without
/// <c>GROUP BY</c> is rejected — top-N ordering on raw rows lands in a later commit.
/// </summary>
public sealed class QueryGroupedOrderByTests
{
    private const string Range = "A1:F11";

    [Fact]
    public void GroupedOrderBy_SumDescending_RanksGroupsByAggregate()
    {
        using var ms = SalesWorkbook.Build();
        using var workbook = ExcelWorkbook.Open(ms);

        QueryResult result = workbook.ExecuteQuery("""
            FROM Sales!A1:F11 HEADER AUTO
            SELECT SUM(NetSales)
            GROUP BY Region
            ORDER BY SUM(NetSales) DESC
            """);

        Assert.Equal(["AMER", "EMEA", "APAC"], result.Rows.Select(r => r.Values.Span[0].ToString()));
    }

    [Fact]
    public void GroupedOrderBy_SumAscending_ReversesRanking()
    {
        using var ms = SalesWorkbook.Build();
        using var workbook = ExcelWorkbook.Open(ms);

        QueryResult result = workbook.ExecuteQuery("""
            FROM Sales!A1:F11 HEADER AUTO
            SELECT SUM(NetSales)
            GROUP BY Region
            ORDER BY SUM(NetSales) ASC
            """);

        Assert.Equal(["APAC", "EMEA", "AMER"], result.Rows.Select(r => r.Values.Span[0].ToString()));
    }

    [Fact]
    public void GroupedOrderBy_NoDirection_DefaultsToAscending()
    {
        using var ms = SalesWorkbook.Build();
        using var workbook = ExcelWorkbook.Open(ms);

        QueryResult result = workbook.ExecuteQuery("""
            FROM Sales!A1:F11 HEADER AUTO
            SELECT SUM(NetSales)
            GROUP BY Region
            ORDER BY SUM(NetSales)
            """);

        Assert.Equal(["APAC", "EMEA", "AMER"], result.Rows.Select(r => r.Values.Span[0].ToString()));
    }

    [Fact]
    public void GroupedOrderBy_DescendingWithLimitOne_ReturnsTopGroupNotFirstSeen()
    {
        using var ms = SalesWorkbook.Build();
        using var workbook = ExcelWorkbook.Open(ms);

        QueryResult unordered = workbook.ExecuteQuery("""
            FROM Sales!A1:F11 HEADER AUTO
            SELECT SUM(NetSales)
            GROUP BY Region
            LIMIT 1
            """);

        QueryResult ordered = workbook.ExecuteQuery("""
            FROM Sales!A1:F11 HEADER AUTO
            SELECT SUM(NetSales)
            GROUP BY Region
            ORDER BY SUM(NetSales) DESC
            LIMIT 1
            """);

        // Without ORDER BY, LIMIT 1 keeps the first-seen group (EMEA); with ORDER BY DESC the
        // ranked top group (AMER) surfaces instead. The pair proves ordering actually changed
        // which row LIMIT keeps.
        Assert.Equal(["EMEA"], unordered.Rows.Select(r => r.Values.Span[0].ToString()));
        Assert.Equal(["AMER"], ordered.Rows.Select(r => r.Values.Span[0].ToString()));
    }

    [Fact]
    public void GroupedOrderBy_GroupKeyAscending_SortsByGroupColumn()
    {
        using var ms = SalesWorkbook.Build();
        using var workbook = ExcelWorkbook.Open(ms);

        QueryResult result = workbook.ExecuteQuery("""
            FROM Sales!A1:F11 HEADER AUTO
            SELECT SUM(NetSales)
            GROUP BY Region
            ORDER BY Region
            """);

        Assert.Equal(["AMER", "APAC", "EMEA"], result.Rows.Select(r => r.Values.Span[0].ToString()));
    }

    [Fact]
    public void GroupedOrderBy_CountDescending_TiesKeepFirstSeenOrder()
    {
        using var ms = SalesWorkbook.Build();
        using var workbook = ExcelWorkbook.Open(ms);

        QueryResult result = workbook.ExecuteQuery("""
            FROM Sales!A1:F11 HEADER AUTO
            SELECT COUNT()
            GROUP BY Region
            ORDER BY COUNT() DESC
            """);

        // EMEA has 4 rows; APAC and AMER are tied at 3 and must resolve to first-seen order
        // (APAC before AMER), not whatever order an unstable sort happens to produce.
        Assert.Equal(["EMEA", "APAC", "AMER"], result.Rows.Select(r => r.Values.Span[0].ToString()));
    }

    [Fact]
    public void GroupedOrderBy_AvgKey_ResolvesDespiteLabelSpelling()
    {
        using var ms = SalesWorkbook.Build();
        using var workbook = ExcelWorkbook.Open(ms);

        // Regression guard: AggregateSpec.Label for AVG is "Average(NetSales)", so a naive
        // string match of the ORDER BY text against the label would reject this query.
        QueryResult result = workbook.ExecuteQuery("""
            FROM Sales!A1:F11 HEADER AUTO
            SELECT AVG(NetSales)
            GROUP BY Region
            ORDER BY AVG(NetSales) DESC
            """);

        Assert.Equal(3, result.Rows.Count);
    }

    [Fact]
    public void GroupedOrderBy_EmptyAggregateResult_SortsLastInBothDirections()
    {
        using var ms = BuildDirtyAggregateWorkbook();
        using var workbook = ExcelWorkbook.Open(ms);

        QueryResult descending = workbook.ExecuteQuery("""
            FROM Sheet1!A1:B6 HEADER ROW 1
            SELECT SUM(Value)
            GROUP BY Grp
            ORDER BY SUM(Value) DESC
            """);

        QueryResult ascending = workbook.ExecuteQuery("""
            FROM Sheet1!A1:B6 HEADER ROW 1
            SELECT SUM(Value)
            GROUP BY Grp
            ORDER BY SUM(Value) ASC
            """);

        // Group "B" holds only non-numeric text in the Value column, so its SUM is Empty.
        // Empty must sort last regardless of direction — never "largest" under DESC.
        Assert.Equal("B", descending.Rows[^1].Values.Span[0].ToString());
        Assert.Equal("B", ascending.Rows[^1].Values.Span[0].ToString());
    }

    [Fact]
    public void GroupedOrderBy_MixedTypeGroupKeys_OrdersNumbersThenTextThenEmpty()
    {
        using var ms = SalesWorkbook.Build();
        using var workbook = ExcelWorkbook.Open(ms);

        QueryResult result = workbook.ExecuteQuery("""
            FROM Sales!A1:F11 HEADER AUTO
            SELECT COUNT()
            GROUP BY NetSales
            ORDER BY NetSales ASC
            """);

        string[] keys = [.. result.Rows.Select(r => r.Values.Span[0].ToString())];

        Assert.Equal(["10", "25.75", "50", "60", "75", "100.5", "200.25", "300", "n/a", "Empty"], keys);
    }

    [Fact]
    public void GroupedOrderBy_MixedTypeGroupKeysDescending_KeepsTextAndEmptyBehindEveryNumber()
    {
        // DESC reverses magnitude within the numeric keys, but must NOT promote the dirty "n/a"
        // text key or the empty key ahead of real numbers — the cross-type rank is fixed and only
        // the same-type comparison inverts. Otherwise ORDER BY <numeric column> DESC LIMIT n would
        // return nothing but garbage cells.
        using var ms = SalesWorkbook.Build();
        using var workbook = ExcelWorkbook.Open(ms);

        QueryResult result = workbook.ExecuteQuery("""
            FROM Sales!A1:F11 HEADER AUTO
            SELECT COUNT()
            GROUP BY NetSales
            ORDER BY NetSales DESC
            """);

        string[] keys = [.. result.Rows.Select(r => r.Values.Span[0].ToString())];

        Assert.Equal(["300", "200.25", "100.5", "75", "60", "50", "25.75", "10", "n/a", "Empty"], keys);
    }

    [Fact]
    public void GroupedOrderBy_UnknownKey_ThrowsWithValidKeysListed()
    {
        var ex = Assert.Throws<QueryDslException>(() => SheetQuerySpec.Parse("""
            FROM Sales!A1:F11 HEADER AUTO
            SELECT SUM(NetSales)
            GROUP BY Region
            ORDER BY Units DESC
            """));

        Assert.Contains("Units", ex.Message, StringComparison.Ordinal);
        Assert.Contains("Region", ex.Message, StringComparison.Ordinal);
        Assert.Contains("Sum(NetSales)", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void GroupedOrderBy_OnSelectStar_Rejected()
    {
        var ex = Assert.Throws<QueryDslException>(() => SheetQuerySpec.Parse($"""
            FROM Sales!{Range} HEADER AUTO
            SELECT *
            ORDER BY Region DESC
            """));

        Assert.Equal(
            "ORDER BY requires LIMIT on row results. Add LIMIT n, or GROUP BY to rank aggregated groups.",
            ex.Message);
    }

    [Fact]
    public void GroupedOrderBy_OnRawColumnProjection_Rejected()
    {
        var ex = Assert.Throws<QueryDslException>(() => SheetQuerySpec.Parse($"""
            FROM Sales!{Range} HEADER AUTO
            SELECT Region, NetSales
            ORDER BY NetSales DESC
            """));

        Assert.Equal(
            "ORDER BY requires LIMIT on row results. Add LIMIT n, or GROUP BY to rank aggregated groups.",
            ex.Message);
    }

    [Fact]
    public void GroupedOrderBy_GlobalAggregateWithoutGroupBy_Rejected()
    {
        var ex = Assert.Throws<QueryDslException>(() => SheetQuerySpec.Parse($"""
            FROM Sales!{Range} HEADER AUTO
            SELECT SUM(NetSales)
            ORDER BY SUM(NetSales) DESC
            """));

        Assert.Equal(
            "ORDER BY is not valid on a global aggregate, which returns a single row. Add GROUP BY to rank aggregated groups.",
            ex.Message);
    }

    [Fact]
    public void GroupedOrderBy_GlobalAggregateWithLimit_StillReportsGlobalAggregate()
    {
        // A LIMIT is present, so the row-results message would be actively misleading here.
        var ex = Assert.Throws<QueryDslException>(() => SheetQuerySpec.Parse($"""
            FROM Sales!{Range} HEADER AUTO
            SELECT SUM(NetSales)
            ORDER BY SUM(NetSales) DESC
            LIMIT 5
            """));

        Assert.Equal(
            "ORDER BY is not valid on a global aggregate, which returns a single row. Add GROUP BY to rank aggregated groups.",
            ex.Message);
    }

    [Fact]
    public void GroupedOrderBy_FluentGlobalAggregate_Rejected()
    {
        using var ms = SalesWorkbook.Build();
        using var workbook = ExcelWorkbook.Open(ms);

        var ex = Assert.Throws<InvalidOperationException>(() => workbook
            .QueryRange(SalesWorkbook.SheetName, Range)
            .Select(QueryAggregates.Sum("NetSales"))
            .OrderBy(QueryAggregates.Sum("NetSales"), descending: true)
            .Take(5)
            .Execute());

        Assert.Contains("global aggregate", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void GroupedOrderBy_FluentApi_MatchesDslResult()
    {
        using var ms = SalesWorkbook.Build();
        using var workbook = ExcelWorkbook.Open(ms);

        QueryResult fluent = workbook
            .QueryRange(SalesWorkbook.SheetName, Range)
            .GroupBy("Region")
            .Select(QueryAggregates.Sum("NetSales"))
            .OrderBy(QueryAggregates.Sum("NetSales"), descending: true)
            .Execute();

        QueryResult dsl = workbook.ExecuteQuery("""
            FROM Sales!A1:F11 HEADER AUTO
            SELECT SUM(NetSales)
            GROUP BY Region
            ORDER BY SUM(NetSales) DESC
            """);

        Assert.Equal(
            dsl.Rows.Select(r => string.Join("|", r.Values.ToArray())),
            fluent.Rows.Select(r => string.Join("|", r.Values.ToArray())));
    }

    [Fact]
    public void GroupedOrderBy_ClauseOutOfOrder_ReportsClauseOrder()
    {
        var ex = Assert.Throws<QueryDslException>(() => SheetQuerySpec.Parse($"""
            FROM Sales!{Range} HEADER AUTO
            SELECT SUM(NetSales)
            GROUP BY Region
            LIMIT 5
            ORDER BY SUM(NetSales) DESC
            """));

        Assert.Contains("GROUP BY, ORDER BY, LIMIT", ex.Message, StringComparison.Ordinal);
    }

    private static MemoryStream BuildDirtyAggregateWorkbook()
    {
        // Group "A": Value 10, 20 (numeric, sum 30). Group "B": Value "x", "y" (non-numeric
        // text, sum is Empty). Group "C": Value 5 (numeric, sum 5). First-seen order: A, B, C.
        // SST: 0=Grp, 1=Value, 2=A, 3=x, 4=y, 5=B, 6=C
        const string sheetXml = """
            <worksheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main">
              <sheetData>
                <row r="1"><c r="A1" t="s"><v>0</v></c><c r="B1" t="s"><v>1</v></c></row>
                <row r="2"><c r="A2" t="s"><v>2</v></c><c r="B2"><v>10</v></c></row>
                <row r="3"><c r="A3" t="s"><v>2</v></c><c r="B3"><v>20</v></c></row>
                <row r="4"><c r="A4" t="s"><v>5</v></c><c r="B4" t="s"><v>3</v></c></row>
                <row r="5"><c r="A5" t="s"><v>5</v></c><c r="B5" t="s"><v>4</v></c></row>
                <row r="6"><c r="A6" t="s"><v>6</v></c><c r="B6"><v>5</v></c></row>
              </sheetData>
            </worksheet>
            """;

        const string sstXml = """
            <sst xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main" uniqueCount="7">
              <si><t>Grp</t></si>
              <si><t>Value</t></si>
              <si><t>A</t></si>
              <si><t>x</t></si>
              <si><t>y</t></si>
              <si><t>B</t></si>
              <si><t>C</t></si>
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
