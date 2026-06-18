using XLSight.Query.Tests.Infrastructure;
using Xunit;

namespace XLSight.Query.Tests;

public sealed class QueryFilterTests
{
    private const string Range = "A1:F11";

    private static int CountWhere(Func<SheetQuery, SheetQuery> configure)
    {
        using var ms = SalesWorkbook.Build();
        using var workbook = ExcelWorkbook.Open(ms);
        QueryResult result = configure(workbook.QueryRange(SalesWorkbook.SheetName, Range))
            .Aggregate(Agg.Count())
            .Execute();
        return (int)result.Rows[0].Values.Span[0].AsNumber();
    }

    [Fact]
    public void Where_TextEquals_MatchesLinqReference()
    {
        int expected = SalesWorkbook.Data.Count(d => d.Region == "EMEA");
        Assert.Equal(expected, CountWhere(q => q.Where("Region", QueryOp.Equals, "EMEA")));
    }

    [Fact]
    public void Where_TextNotEquals_MatchesLinqReference()
    {
        int expected = SalesWorkbook.Data.Count(d => d.Region != "EMEA");
        Assert.Equal(expected, CountWhere(q => q.Where("Region", QueryOp.NotEquals, "EMEA")));
    }

    [Fact]
    public void Where_NumberOrderingOperators_MatchLinqReference()
    {
        Assert.Equal(
            SalesWorkbook.Data.Count(d => d.Units > 5),
            CountWhere(q => q.Where("Units", QueryOp.GreaterThan, 5)));
        Assert.Equal(
            SalesWorkbook.Data.Count(d => d.Units <= 3),
            CountWhere(q => q.Where("Units", QueryOp.LessThanOrEqual, 3)));
        Assert.Equal(
            SalesWorkbook.Data.Count(d => d.Units >= 7),
            CountWhere(q => q.Where("Units", QueryOp.GreaterThanOrEqual, 7)));
        Assert.Equal(
            SalesWorkbook.Data.Count(d => d.Units < 2),
            CountWhere(q => q.Where("Units", QueryOp.LessThan, 2)));
    }

    [Fact]
    public void Where_DateGreaterThanOrEqual_MatchesLinqReference()
    {
        var cutoff = new DateTime(2024, 3, 1);
        int expected = SalesWorkbook.Data.Count(d => d.OrderDate >= cutoff);
        Assert.Equal(expected, CountWhere(q => q.Where("OrderDate", QueryOp.GreaterThanOrEqual, cutoff)));
    }

    [Fact]
    public void Where_BooleanEquals_MatchesLinqReference()
    {
        int expected = SalesWorkbook.Data.Count(d => d.OnPromo);
        Assert.Equal(expected, CountWhere(q => q.Where("OnPromo", QueryOp.Equals, true)));
    }

    [Fact]
    public void Where_MultipleFilters_AreAndCombined()
    {
        int expected = SalesWorkbook.Data.Count(d => d.Region == "EMEA" && d.Units > 1);
        Assert.Equal(expected, CountWhere(q => q
            .Where("Region", QueryOp.Equals, "EMEA")
            .Where("Units", QueryOp.GreaterThan, 1)));
    }

    [Fact]
    public void Where_NumberLiteralAgainstTextCell_NeverMatches()
    {
        // The dirty "n/a" cell in NetSales must not match any numeric predicate, including NotEquals.
        int expected = SalesWorkbook.Data.Count(d => d.NetSales is { } v && v != 50);
        Assert.Equal(expected, CountWhere(q => q.Where("NetSales", QueryOp.NotEquals, 50)));
    }

    [Fact]
    public void Where_ColumnLookup_FallsBackToCaseInsensitive()
    {
        int expected = SalesWorkbook.Data.Count(d => d.Region == "EMEA");
        Assert.Equal(expected, CountWhere(q => q.Where("region", QueryOp.Equals, "EMEA")));
    }

    [Fact]
    public void Where_BooleanOrderingOperator_ThrowsAtBuildTime()
    {
        using var ms = SalesWorkbook.Build();
        using var workbook = ExcelWorkbook.Open(ms);
        var query = workbook.QueryRange(SalesWorkbook.SheetName, Range);

        Assert.Throws<ArgumentException>(() => query.Where("OnPromo", QueryOp.LessThan, true));
    }
}
