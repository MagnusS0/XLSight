using XLSight.Query.Tests.Infrastructure;
using Xunit;

namespace XLSight.Query.Tests;

public sealed class QueryAggregateTests
{
    private const string Range = "A1:F11";

    [Fact]
    public void GroupBySum_FilteredByRegion_MatchesLinqReference()
    {
        var expected = SalesWorkbook.Data
            .Where(d => d.Region == "EMEA")
            .GroupBy(d => d.Month)
            .ToDictionary(g => g.Key, g => g.Sum(d => d.Units), StringComparer.Ordinal);

        using var ms = SalesWorkbook.Build();
        using var workbook = ExcelWorkbook.Open(ms);

        QueryResult result = workbook
            .QueryRange(SalesWorkbook.SheetName, Range)
            .Where("Region", QueryOperator.Equals, "EMEA")
            .GroupBy("Month")
            .Select(QueryAggregates.Sum("Units"))
            .Execute();

        Assert.Equal(["Month", "Sum(Units)"], result.Columns);
        var actual = result.Rows.ToDictionary(
            r => r.Values.Span[0].AsText(), r => r.Values.Span[1].AsNumber(), StringComparer.Ordinal);
        Assert.Equal(expected, actual);
    }

    [Fact]
    public void GlobalAggregates_MatchLinqReference()
    {
        double[] units = [.. SalesWorkbook.Data.Select(d => d.Units)];

        using var ms = SalesWorkbook.Build();
        using var workbook = ExcelWorkbook.Open(ms);

        QueryResult result = workbook
            .QueryRange(SalesWorkbook.SheetName, Range)
            .Select(QueryAggregates.Sum("Units"), QueryAggregates.Count(), QueryAggregates.Min("Units"), QueryAggregates.Max("Units"), QueryAggregates.Average("Units"))
            .Execute();

        var row = Assert.Single(result.Rows);
        Assert.Equal(units.Sum(), row.Values.Span[0].AsNumber());
        Assert.Equal(units.Length, row.Values.Span[1].AsNumber());
        Assert.Equal(units.Min(), row.Values.Span[2].AsNumber());
        Assert.Equal(units.Max(), row.Values.Span[3].AsNumber());
        Assert.Equal(units.Average(), row.Values.Span[4].AsNumber());
    }

    [Fact]
    public void Average_SkipsDirtyAndMissingCells()
    {
        double[] clean = [.. SalesWorkbook.Data.Where(d => d.NetSales is not null).Select(d => d.NetSales!.Value)];

        using var ms = SalesWorkbook.Build();
        using var workbook = ExcelWorkbook.Open(ms);

        QueryResult result = workbook
            .QueryRange(SalesWorkbook.SheetName, Range)
            .Select(QueryAggregates.Average("NetSales"))
            .Execute();

        Assert.Equal(clean.Average(), result.Rows[0].Values.Span[0].AsNumber());
    }

    [Fact]
    public void MinMax_OnDateColumn_ReturnDateCells()
    {
        using var ms = SalesWorkbook.Build();
        using var workbook = ExcelWorkbook.Open(ms);

        QueryResult result = workbook
            .QueryRange(SalesWorkbook.SheetName, Range)
            .Select(QueryAggregates.Min("OrderDate"), QueryAggregates.Max("OrderDate"))
            .Execute();

        var row = Assert.Single(result.Rows);
        Assert.Equal(SalesWorkbook.Data.Min(d => d.OrderDate), row.Values.Span[0].AsDate());
        Assert.Equal(SalesWorkbook.Data.Max(d => d.OrderDate), row.Values.Span[1].AsDate());
    }

    [Fact]
    public void Sum_GroupWithNoNumericCells_ReturnsEmptyCell()
    {
        // EMEA/Mar has only a missing NetSales cell, so its sum has no inputs.
        using var ms = SalesWorkbook.Build();
        using var workbook = ExcelWorkbook.Open(ms);

        QueryResult result = workbook
            .QueryRange(SalesWorkbook.SheetName, Range)
            .Where("Region", QueryOperator.Equals, "EMEA")
            .GroupBy("Month")
            .Select(QueryAggregates.Sum("NetSales"))
            .Execute();

        var marchRow = result.Rows.Single(r => r.Values.Span[0].AsText() == "Mar");
        Assert.True(marchRow.Values.Span[1].IsEmpty);
    }

    [Fact]
    public async Task ExecuteAsync_ProducesSameResultAsSync()
    {
        using var ms = SalesWorkbook.Build();
        using var workbook = ExcelWorkbook.Open(ms);

        QueryResult sync = workbook
            .QueryRange(SalesWorkbook.SheetName, Range)
            .GroupBy("Region")
            .Select(QueryAggregates.Sum("Units"), QueryAggregates.Count())
            .Execute();

        QueryResult async = await workbook
            .QueryRange(SalesWorkbook.SheetName, Range)
            .GroupBy("Region")
            .Select(QueryAggregates.Sum("Units"), QueryAggregates.Count())
            .ExecuteAsync(TestContext.Current.CancellationToken);

        Assert.Equal(sync.Columns, async.Columns);
        Assert.Equal(sync.RowsScanned, async.RowsScanned);
        Assert.Equal(sync.RowsMatched, async.RowsMatched);
        Assert.Equal(
            sync.Rows.Select(r => string.Join("|", r.Values.ToArray())),
            async.Rows.Select(r => string.Join("|", r.Values.ToArray())));
    }
}
