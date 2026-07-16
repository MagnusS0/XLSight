using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Jobs;
using XLSight;

/// <summary>
/// Agent-shaped workload: many small reads against a single open workbook.
/// Exercises the per-read entry-open path rather than workbook open or
/// full-sheet scan throughput.
/// </summary>
[MemoryDiagnoser]
[ShortRunJob]
public class RepeatedReadBenchmarks
{
    private ExcelWorkbook _medium = null!;

    [GlobalSetup]
    public void Setup() =>
        _medium = ExcelWorkbook.Open(Path.Combine(AppContext.BaseDirectory, "TestData", "medium.xlsx"));

    [GlobalCleanup]
    public void Cleanup() => _medium.Dispose();

    [Benchmark]
    public ExcelCellValue ReadCell_Medium_x100()
    {
        ExcelCellValue last = default;
        for (int i = 0; i < 100; i++)
        {
            last = _medium.ReadCell("Data", "C5");
        }

        return last;
    }

    [Benchmark]
    public int PeekRange_Medium_x20()
    {
        int rows = 0;
        for (int i = 0; i < 20; i++)
        {
            rows += _medium.ReadRange("Data", "A1:J10").Rows.Count;
        }

        return rows;
    }
}
