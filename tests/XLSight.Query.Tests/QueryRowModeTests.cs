using XLSight.Query.Tests.Infrastructure;
using Xunit;

namespace XLSight.Query.Tests;

public sealed class QueryRowModeTests
{
    private const string Range = "A1:F11";

    [Fact]
    public void Execute_WithoutAggregates_ReturnsMatchingRowsWithSourceIndices()
    {
        int[] expectedRows = [.. SalesWorkbook.Data
            .Select((d, i) => (d, Row: SalesWorkbook.SheetRowOf(i)))
            .Where(x => x.d.Region == "APAC")
            .Select(x => x.Row)];

        using var ms = SalesWorkbook.Build();
        using var workbook = ExcelWorkbook.Open(ms);

        QueryResult result = workbook
            .QueryRange(SalesWorkbook.SheetName, Range)
            .Where("Region", QueryOp.Equals, "APAC")
            .Execute();

        Assert.Equal(SalesWorkbook.Headers, result.Columns);
        Assert.Equal(expectedRows, result.Rows.Select(r => r.SourceRowIndex!.Value));
        Assert.All(result.Rows, r => Assert.Equal("APAC", r.Values.Span[0].AsText()));
    }

    [Fact]
    public void Limit_WithoutFilter_StopsScanAfterEnoughRows()
    {
        using var ms = SalesWorkbook.Build();
        using var workbook = ExcelWorkbook.Open(ms);

        QueryResult result = workbook
            .QueryRange(SalesWorkbook.SheetName, Range)
            .Limit(3)
            .Execute();

        Assert.Equal(3, result.Rows.Count);
        Assert.Equal(3, result.RowsScanned);
    }

    [Fact]
    public void Limit_WithFilter_StopsScanAtNthMatch()
    {
        using var ms = SalesWorkbook.Build();
        using var workbook = ExcelWorkbook.Open(ms);

        QueryResult result = workbook
            .QueryRange(SalesWorkbook.SheetName, Range)
            .Where("Region", QueryOp.Equals, "EMEA")
            .Limit(2)
            .Execute();

        // The first two EMEA records are the first two data rows, so the scan
        // stops without reading the rest of the range.
        Assert.Equal(2, result.Rows.Count);
        Assert.Equal(2, result.RowsScanned);
        Assert.Equal([SalesWorkbook.SheetRowOf(0), SalesWorkbook.SheetRowOf(1)], result.Rows.Select(r => r.SourceRowIndex!.Value));
    }

    [Fact]
    public void Limit_OnGroupedQuery_TruncatesGroupsInFirstSeenOrder()
    {
        using var ms = SalesWorkbook.Build();
        using var workbook = ExcelWorkbook.Open(ms);

        QueryResult result = workbook
            .QueryRange(SalesWorkbook.SheetName, Range)
            .GroupBy("Region")
            .Aggregate(Agg.Count())
            .Limit(2)
            .Execute();

        Assert.Equal(2, result.Rows.Count);
        Assert.Equal(["EMEA", "APAC"], result.Rows.Select(r => r.Values.Span[0].AsText()));
        // The full range is still scanned: later rows can update any group.
        Assert.Equal(SalesWorkbook.Data.Length, result.RowsScanned);
    }

    [Fact]
    public void HeaderRow_ExplicitRow_SkipsLeadingBannerRows()
    {
        using var ms = SalesWorkbook.Build(titleRow: true);
        using var workbook = ExcelWorkbook.Open(ms);

        QueryResult result = workbook
            .QueryRange(SalesWorkbook.SheetName, "A1:F12", headerRow: 2)
            .Where("Region", QueryOp.Equals, "EMEA")
            .Aggregate(Agg.Count())
            .Execute();

        Assert.Equal(SalesWorkbook.Data.Count(d => d.Region == "EMEA"), result.Rows[0].Values.Span[0].AsNumber());
    }

    [Fact]
    public void HeaderRow_Defaulted_UsesFirstNonEmptyRowOfRange()
    {
        using var ms = SalesWorkbook.Build(titleRow: true);
        using var workbook = ExcelWorkbook.Open(ms);

        // Range starts below the banner, so the header row is found implicitly.
        QueryResult result = workbook
            .QueryRange(SalesWorkbook.SheetName, "A2:F12")
            .GroupBy("Region")
            .Aggregate(Agg.Count())
            .Execute();

        Assert.Equal(3, result.Rows.Count);
    }

    [Fact]
    public void HeaderRow_OutsideRange_Throws()
    {
        using var ms = SalesWorkbook.Build();
        using var workbook = ExcelWorkbook.Open(ms);

        Assert.Throws<ArgumentOutOfRangeException>(
            () => workbook.QueryRange(SalesWorkbook.SheetName, "A2:F11", headerRow: 1));
    }
}
