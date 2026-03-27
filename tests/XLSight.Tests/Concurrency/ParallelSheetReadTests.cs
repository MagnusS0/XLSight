using Xunit;

namespace XLSight.Tests.Concurrency;

public sealed class ParallelSheetReadTests
{
    private static string TestFilePath(string fileName) =>
        Path.Combine(AppContext.BaseDirectory, "TestData", fileName);

    [Fact]
    public async Task CanReadTwoSheetsInParallel_FileBacked()
    {
        var path = TestFilePath("small.xlsx");

        using var wb = XLSight.ExcelWorkbook.Open(path);

        var t1 = Task.Run(() => wb.StreamSheet("Sheet1").Count());
        var t2 = Task.Run(() => wb.AnalyzeSheet("Sheet1"));

        // Must not throw InvalidOperationException for file-backed workbooks.
        await Task.WhenAll(t1, t2);
    }

    [Fact]
    public async Task CanReadTwoSheetsInParallel_MultipleAnalyze()
    {
        var path = TestFilePath("small.xlsx");

        using var wb = XLSight.ExcelWorkbook.Open(path);

        var t1 = Task.Run(() => wb.AnalyzeSheet("Sheet1"));
        var t2 = Task.Run(() => wb.AnalyzeSheet("EmptySheet"));

        await Task.WhenAll(t1, t2);
    }

    [Fact]
    public async Task CanReadTwoSheetsInParallel_AnalyzeAndReadRange()
    {
        var path = TestFilePath("small.xlsx");

        using var wb = XLSight.ExcelWorkbook.Open(path);

        var t1 = Task.Run(() => wb.AnalyzeSheet("Sheet1"));
        var t2 = Task.Run(() => wb.ReadRange("Sheet1", "A1:B2"));

        await Task.WhenAll(t1, t2);
    }
}
