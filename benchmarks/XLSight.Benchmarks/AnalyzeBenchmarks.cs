using BenchmarkDotNet.Attributes;
using XLSight;
using XLSight.Models.Analysis;

[MemoryDiagnoser]
[SimpleJob]
public class AnalyzeBenchmarks
{
    private string _smallPath       = null!;
    private string _mediumPath      = null!;
    private string _namedRangesPath = null!;

    [GlobalSetup]
    public void Setup()
    {
        _smallPath       = Path.Combine(AppContext.BaseDirectory, "TestData", "small.xlsx");
        _mediumPath      = Path.Combine(AppContext.BaseDirectory, "TestData", "medium.xlsx");
        _namedRangesPath = Path.Combine(AppContext.BaseDirectory, "TestData", "named_ranges.xlsx");
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
}
