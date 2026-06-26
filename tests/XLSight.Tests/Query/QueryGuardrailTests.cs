using XLSight.Analysis;
using Xunit;

namespace XLSight.Query.Tests;

public sealed class QueryGuardrailTests
{
    private const string Range = "A1:F11";

    [Fact]
    public void Sum_DirtyTextCell_SkippedAndReportedWithProvenance()
    {
        double expected = SalesWorkbook.Data.Where(d => d.NetSales is not null).Sum(d => d.NetSales!.Value);
        int dirtyRecordIndex = Array.FindIndex(SalesWorkbook.Data, d => d.NetSalesText is not null);

        using var ms = SalesWorkbook.Build();
        using var workbook = ExcelWorkbook.Open(ms);

        QueryResult result = workbook
            .QueryRange(SalesWorkbook.SheetName, Range)
            .Select(QueryAggregates.Sum("NetSales"))
            .Execute();

        Assert.Equal(expected, result.Rows[0].Values.Span[0].AsNumber());
        var dirty = Assert.Single(result.Unaggregatable);
        Assert.Equal("NetSales", dirty.Column);
        Assert.Equal(1, dirty.SkippedCount);
        Assert.Equal([SalesWorkbook.SheetRowOf(dirtyRecordIndex)], dirty.SampleRowIndices);
    }

    [Fact]
    public void GroupBy_ExceedingGroupLimit_ThrowsTooManyGroups()
    {
        using var ms = SalesWorkbook.Build();
        using var workbook = ExcelWorkbook.Open(ms);

        var query = workbook
            .QueryRange(SalesWorkbook.SheetName, Range)
            .GroupBy("Region")
            .Select(QueryAggregates.Count())
            .WithGroupLimit(2);

        var ex = Assert.Throws<TooManyGroupsException>(() => query.Execute());
        Assert.Contains("WithGroupLimit", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void UnknownColumn_ThrowsListingAvailableColumns()
    {
        using var ms = SalesWorkbook.Build();
        using var workbook = ExcelWorkbook.Open(ms);

        var query = workbook
            .QueryRange(SalesWorkbook.SheetName, Range)
            .Where("Regin", QueryOperator.Equals, "EMEA")
            .Select(QueryAggregates.Count());

        var ex = Assert.Throws<InvalidOperationException>(() => query.Execute());
        Assert.Contains("Regin", ex.Message, StringComparison.Ordinal);
        Assert.Contains("Region", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void GroupBy_WithoutAggregate_ThrowsWithGuidance()
    {
        using var ms = SalesWorkbook.Build();
        using var workbook = ExcelWorkbook.Open(ms);

        var query = workbook.QueryRange(SalesWorkbook.SheetName, Range).GroupBy("Region");

        var ex = Assert.Throws<InvalidOperationException>(() => query.Execute());
        Assert.Contains("DistinctValues", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void WithStats_ImpossibleNumericPredicate_ReturnsEmptyWithoutScanning()
    {
        using var ms = SalesWorkbook.Build();
        using var workbook = ExcelWorkbook.Open(ms);

        QueryResult result = workbook
            .QueryRange(SalesWorkbook.SheetName, Range)
            .Where("Units", QueryOperator.GreaterThan, 10)
            .Select(QueryAggregates.Count())
            .WithStats([UnitsProfile(min: 1, max: 10)])
            .Execute();

        Assert.Empty(result.Rows);
        Assert.Equal(0, result.RowsScanned);
    }

    [Fact]
    public void WithStats_SatisfiablePredicate_StillScans()
    {
        using var ms = SalesWorkbook.Build();
        using var workbook = ExcelWorkbook.Open(ms);

        QueryResult result = workbook
            .QueryRange(SalesWorkbook.SheetName, Range)
            .Where("Units", QueryOperator.GreaterThan, 5)
            .Select(QueryAggregates.Count())
            .WithStats([UnitsProfile(min: 1, max: 10)])
            .Execute();

        Assert.Equal(SalesWorkbook.Data.Length, result.RowsScanned);
        Assert.Equal(SalesWorkbook.Data.Count(d => d.Units > 5), result.Rows[0].Values.Span[0].AsNumber());
    }

    [Fact]
    public void StatsPruning_MatchesFootnoteMarkedHeader()
    {
        // The profiled header is "Units*" (footnote-marked). A filter referencing "Units" (no marker)
        // must still be matched during stats pruning via NormalizeHeaderName stripping the asterisk.
        // The filter Units > max+1 is provably empty, so the result is pruned without scanning.
        using var ms = SalesWorkbook.Build();
        using var workbook = ExcelWorkbook.Open(ms);

        QueryResult result = workbook
            .QueryRange(SalesWorkbook.SheetName, Range)
            .Where("Units", QueryOperator.GreaterThan, 10)
            .Select(QueryAggregates.Count())
            .WithStats([UnitsProfileFootnoted(min: 1, max: 10)])
            .Execute();

        Assert.Empty(result.Rows);
        Assert.Equal(0, result.RowsScanned);
    }

    private static ColumnProfile UnitsProfile(double min, double max) => new()
    {
        ColumnIndex = 4,
        InferredHeader = "Units",
        DominantType = CellType.Number,
        NonEmptyCount = SalesWorkbook.Data.Length,
        TextCount = 0,
        NumberCount = SalesWorkbook.Data.Length,
        DateCount = 0,
        BooleanCount = 0,
        DistinctValueEstimate = SalesWorkbook.Data.Length,
        MinNumericValue = min,
        MaxNumericValue = max,
        MaxTextLength = null,
        HasFormulas = false,
    };

    private static ColumnProfile UnitsProfileFootnoted(double min, double max) => new()
    {
        ColumnIndex = 4,
        InferredHeader = "Units*",
        DominantType = CellType.Number,
        NonEmptyCount = SalesWorkbook.Data.Length,
        TextCount = 0,
        NumberCount = SalesWorkbook.Data.Length,
        DateCount = 0,
        BooleanCount = 0,
        DistinctValueEstimate = SalesWorkbook.Data.Length,
        MinNumericValue = min,
        MaxNumericValue = max,
        MaxTextLength = null,
        HasFormulas = false,
    };
}
