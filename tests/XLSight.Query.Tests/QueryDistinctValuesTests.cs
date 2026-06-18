using XLSight.Query.Tests.Infrastructure;
using Xunit;

namespace XLSight.Query.Tests;

public sealed class QueryDistinctValuesTests
{
    private const string Range = "A1:F11";

    [Fact]
    public void DistinctValues_OrderedByFrequencyThenValue()
    {
        using var ms = SalesWorkbook.Build();
        using var workbook = ExcelWorkbook.Open(ms);

        IReadOnlyList<DistinctValueCount> values = workbook
            .QueryRange(SalesWorkbook.SheetName, Range)
            .DistinctValues("Region");

        // EMEA appears 4 times; AMER and APAC tie at 3 and sort ordinally.
        Assert.Equal(
            [new("EMEA", 4), new("AMER", 3), new("APAC", 3)],
            values);
    }

    [Fact]
    public void DistinctValues_RespectsFilters()
    {
        using var ms = SalesWorkbook.Build();
        using var workbook = ExcelWorkbook.Open(ms);

        IReadOnlyList<DistinctValueCount> values = workbook
            .QueryRange(SalesWorkbook.SheetName, Range)
            .Where("Month", QueryOperator.Equals, "Jan")
            .DistinctValues("Region");

        Assert.Equal([new("EMEA", 2), new("AMER", 1), new("APAC", 1)], values);
    }

    [Fact]
    public void DistinctValues_TopCapsResultCount()
    {
        using var ms = SalesWorkbook.Build();
        using var workbook = ExcelWorkbook.Open(ms);

        IReadOnlyList<DistinctValueCount> values = workbook
            .QueryRange(SalesWorkbook.SheetName, Range)
            .DistinctValues("Region", top: 1);

        Assert.Equal([new DistinctValueCount("EMEA", 4)], values);
    }

    [Fact]
    public async Task DistinctValuesAsync_MatchesSync()
    {
        using var ms = SalesWorkbook.Build();
        using var workbook = ExcelWorkbook.Open(ms);

        IReadOnlyList<DistinctValueCount> sync = workbook
            .QueryRange(SalesWorkbook.SheetName, Range)
            .DistinctValues("Month");

        IReadOnlyList<DistinctValueCount> async = await workbook
            .QueryRange(SalesWorkbook.SheetName, Range)
            .DistinctValuesAsync("Month", ct: TestContext.Current.CancellationToken);

        Assert.Equal(sync, async);
    }

    [Fact]
    public void DistinctValues_ExceedingCap_ThrowsTooManyGroups()
    {
        using var ms = SalesWorkbook.Build();
        using var workbook = ExcelWorkbook.Open(ms);

        var query = workbook
            .QueryRange(SalesWorkbook.SheetName, Range)
            .WithGroupLimit(2);

        Assert.Throws<TooManyGroupsException>(() => query.DistinctValues("Region"));
    }
}
