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
            .Where("Region", QueryOp.Equals, "EMEA")
            .GroupBy("Month")
            .Aggregate(Agg.Sum("Units"))
            .Execute();

        Assert.Equal(["Month", "Sum(Units)"], result.Columns);
        var actual = result.Rows.ToDictionary(
            r => r.Values[0].AsText(), r => r.Values[1].AsNumber(), StringComparer.Ordinal);
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
            .Aggregate(Agg.Sum("Units"), Agg.Count(), Agg.Min("Units"), Agg.Max("Units"), Agg.Avg("Units"))
            .Execute();

        var row = Assert.Single(result.Rows);
        Assert.Equal(units.Sum(), row.Values[0].AsNumber());
        Assert.Equal(units.Length, row.Values[1].AsNumber());
        Assert.Equal(units.Min(), row.Values[2].AsNumber());
        Assert.Equal(units.Max(), row.Values[3].AsNumber());
        Assert.Equal(units.Average(), row.Values[4].AsNumber());
    }

    [Fact]
    public void Average_SkipsDirtyAndMissingCells()
    {
        double[] clean = [.. SalesWorkbook.Data.Where(d => d.NetSales is not null).Select(d => d.NetSales!.Value)];

        using var ms = SalesWorkbook.Build();
        using var workbook = ExcelWorkbook.Open(ms);

        QueryResult result = workbook
            .QueryRange(SalesWorkbook.SheetName, Range)
            .Aggregate(Agg.Avg("NetSales"))
            .Execute();

        Assert.Equal(clean.Average(), result.Rows[0].Values[0].AsNumber());
    }

    [Fact]
    public void MinMax_OnDateColumn_ReturnDateCells()
    {
        using var ms = SalesWorkbook.Build();
        using var workbook = ExcelWorkbook.Open(ms);

        QueryResult result = workbook
            .QueryRange(SalesWorkbook.SheetName, Range)
            .Aggregate(Agg.Min("OrderDate"), Agg.Max("OrderDate"))
            .Execute();

        var row = Assert.Single(result.Rows);
        Assert.Equal(SalesWorkbook.Data.Min(d => d.OrderDate), row.Values[0].AsDate());
        Assert.Equal(SalesWorkbook.Data.Max(d => d.OrderDate), row.Values[1].AsDate());
    }

    [Fact]
    public void Sum_GroupWithNoNumericCells_ReturnsEmptyCell()
    {
        // EMEA/Mar has only a missing NetSales cell, so its sum has no inputs.
        using var ms = SalesWorkbook.Build();
        using var workbook = ExcelWorkbook.Open(ms);

        QueryResult result = workbook
            .QueryRange(SalesWorkbook.SheetName, Range)
            .Where("Region", QueryOp.Equals, "EMEA")
            .GroupBy("Month")
            .Aggregate(Agg.Sum("NetSales"))
            .Execute();

        var marchRow = result.Rows.Single(r => r.Values[0].AsText() == "Mar");
        Assert.True(marchRow.Values[1].IsEmpty);
    }

    [Fact]
    public async Task ExecuteAsync_ProducesSameResultAsSync()
    {
        using var ms = SalesWorkbook.Build();
        using var workbook = ExcelWorkbook.Open(ms);

        QueryResult sync = workbook
            .QueryRange(SalesWorkbook.SheetName, Range)
            .GroupBy("Region")
            .Aggregate(Agg.Sum("Units"), Agg.Count())
            .Execute();

        QueryResult async = await workbook
            .QueryRange(SalesWorkbook.SheetName, Range)
            .GroupBy("Region")
            .Aggregate(Agg.Sum("Units"), Agg.Count())
            .ExecuteAsync(TestContext.Current.CancellationToken);

        Assert.Equal(sync.Columns, async.Columns);
        Assert.Equal(sync.RowsScanned, async.RowsScanned);
        Assert.Equal(sync.RowsMatched, async.RowsMatched);
        Assert.Equal(
            sync.Rows.Select(r => string.Join("|", r.Values)),
            async.Rows.Select(r => string.Join("|", r.Values)));
    }
}
