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
}
