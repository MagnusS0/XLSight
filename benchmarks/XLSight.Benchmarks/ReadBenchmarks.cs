using BenchmarkDotNet.Attributes;
using XLSight;

[MemoryDiagnoser]
[SimpleJob]
public class ReadBenchmarks
{
    private string _smallPath = null!;
    private string _mediumPath = null!;

    [GlobalSetup]
    public void Setup()
    {
        _smallPath  = Path.Combine(AppContext.BaseDirectory, "TestData", "small.xlsx");
        _mediumPath = Path.Combine(AppContext.BaseDirectory, "TestData", "medium.xlsx");
    }

    [Benchmark]
    public ExcelCellValue ReadCell_Small()
    {
        using var wb = ExcelWorkbook.Open(_smallPath);
        return wb.ReadCell("Sheet1", "C5");
    }

    [Benchmark]
    public RangeResult ReadRange_Small_5x10()
    {
        using var wb = ExcelWorkbook.Open(_smallPath);
        return wb.ReadRange("Sheet1", "A1:E10");
    }

    [Benchmark]
    public RangeResult ReadRange_Medium_10x1000()
    {
        using var wb = ExcelWorkbook.Open(_mediumPath);
        return wb.ReadRange("Data", "A1:J1000");
    }
}
