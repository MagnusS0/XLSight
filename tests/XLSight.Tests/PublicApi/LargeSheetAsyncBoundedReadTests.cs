using Xunit;

namespace XLSight.Tests.PublicApi;

/// <summary>
/// Async bounded reads deep into a sheet whose XML exceeds the scan window's growth limit.
/// Rows skipped before the range must stay committed across refills; otherwise each refill
/// rewinds to the first skipped row and the window grows until the row-size guard throws.
/// </summary>
public sealed class LargeSheetAsyncBoundedReadTests
{
    private static string LargePath => Path.Combine(AppContext.BaseDirectory, "TestData", "large.xlsx");

    [Fact]
    public async Task ReadCellAsync_LastRowOfLargeSheet_MatchesSync()
    {
        using var workbook = ExcelWorkbook.Open(LargePath);

        ExcelCellValue expected = workbook.ReadCell("Numbers", "E100001");
        ExcelCellValue actual = await workbook.ReadCellAsync("Numbers", "E100001", ct: TestContext.Current.CancellationToken);

        Assert.Equal(CellType.Number, expected.CellType);
        Assert.Equal(expected, actual);
    }

    [Fact]
    public async Task ReadRangeAsync_MiddleOfLargeSheet_MatchesSync()
    {
        using var workbook = ExcelWorkbook.Open(LargePath);

        RangeResult expected = workbook.ReadRange("Numbers", "A50001:E50100");
        RangeResult actual = await workbook.ReadRangeAsync("Numbers", "A50001:E50100", ct: TestContext.Current.CancellationToken);

        Assert.Equal(expected.Cells.ToArray(), actual.Cells.ToArray());
    }
}
