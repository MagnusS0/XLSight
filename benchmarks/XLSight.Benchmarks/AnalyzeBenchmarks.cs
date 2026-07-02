using BenchmarkDotNet.Attributes;
using XLSight;
using XLSight.Analysis;

[MemoryDiagnoser]
[SimpleJob]
public class AnalyzeBenchmarks
{
    private string  _smallPath       = null!;
    private string  _mediumPath      = null!;
    private string  _namedRangesPath = null!;
    private string  _complexPath     = null!;
    private string  _xlLargePath     = null!;

    [GlobalSetup]
    public void Setup()
    {
        _smallPath       = Path.Combine(AppContext.BaseDirectory, "TestData", "small.xlsx");
        _mediumPath      = Path.Combine(AppContext.BaseDirectory, "TestData", "medium.xlsx");
        _namedRangesPath = Path.Combine(AppContext.BaseDirectory, "TestData", "named_ranges.xlsx");
        _complexPath     = Path.Combine(AppContext.BaseDirectory, "TestData", "complex_workbook.xlsx");
        _xlLargePath     = BenchmarkFixture.OptionalPath("xl_large.xlsx");
    }

    [Benchmark]
    public SheetInfo AnalyzeSheet_Small()
    {
        using var wb = ExcelWorkbook.Open(_smallPath);
        return wb.AnalyzeSheet("Sheet1");
    }

    [Benchmark]
    public SheetInfo AnalyzeSheet_Medium()
    {
        using var wb = ExcelWorkbook.Open(_mediumPath);
        return wb.AnalyzeSheet("Data");
    }

    [Benchmark]
    public WorkbookInfo AnalyzeWorkbook_NamedRanges()
    {
        using var wb = ExcelWorkbook.Open(_namedRangesPath);
        return wb.Analyze();
    }

    /// <summary>
    /// Full workbook analysis on complex_workbook.xlsx — mixed text/numeric
    /// scenario sheets with non-trivial layouts, the closest fixture to real
    /// financial workbooks. Exercises layout inference beyond simple tables.
    /// </summary>
    [Benchmark]
    public WorkbookInfo AnalyzeWorkbook_Complex()
    {
        using var wb = ExcelWorkbook.Open(_complexPath);
        return wb.Analyze();
    }

    /// <summary>
    /// Full workbook analysis on xl_large.xlsx — 4 sheets, ~1.1M total rows.
    /// The dominant sheet (Worksheet) has ~986K rows × 8 cols; the others add
    /// 27K, 4K, and 100K rows. Exercises the full Analyze() path at scale.
    /// Run explicitly with a filter when xl_large.xlsx is present (excluded from source control).
    /// </summary>
    [Benchmark]
    public WorkbookInfo AnalyzeWorkbook_XlLarge()
    {
        using var wb = ExcelWorkbook.Open(BenchmarkFixture.RequireOptionalLargeFixture(_xlLargePath));
        return wb.Analyze();
    }
}
