using BenchmarkDotNet.Attributes;
using XLSight;

[MemoryDiagnoser]
[SimpleJob]
public class StreamBenchmarks
{
    private string _largePath       = null!;
    private string _stringHeavyPath = null!;
    private string? _xlLargePath;

    [GlobalSetup]
    public void Setup()
    {
        _largePath       = Path.Combine(AppContext.BaseDirectory, "TestData", "large.xlsx");
        _stringHeavyPath = Path.Combine(AppContext.BaseDirectory, "TestData", "string_heavy.xlsx");
        var xlLarge      = Path.Combine(AppContext.BaseDirectory, "TestData", "xl_large.xlsx");
        _xlLargePath     = File.Exists(xlLarge) ? xlLarge : null;
    }

    [Benchmark]
    public int StreamSheet_Large_AllRows()
    {
        using var wb = ExcelWorkbook.Open(_largePath);
        int count = 0;
        foreach (var _ in wb.StreamSheet("Numbers"))
        {
            count++;
        }
        return count;
    }

    [Benchmark]
    public int StreamSheet_Large_First10()
    {
        using var wb = ExcelWorkbook.Open(_largePath);
        int count = 0;
        foreach (var _ in wb.StreamSheet("Numbers").Take(10))
        {
            count++;
        }
        return count;
    }

    [Benchmark]
    public int StreamSheet_StringHeavy()
    {
        using var wb = ExcelWorkbook.Open(_stringHeavyPath);
        int count = 0;
        foreach (var _ in wb.StreamSheet("Strings"))
        {
            count++;
        }
        return count;
    }

    [Benchmark]
    public int StreamSheet_XlLarge_AllRows()
    {
        if (_xlLargePath is null)
        {
            return -1;
        }

        using var wb = ExcelWorkbook.Open(_xlLargePath);
        int count = 0;
        foreach (var _ in wb.StreamSheet("Worksheet"))
        {
            count++;
        }
        return count;
    }

    [Benchmark]
    public int StreamSheet_XlLarge_First10()
    {
        if (_xlLargePath is null)
        {
            return -1;
        }

        using var wb = ExcelWorkbook.Open(_xlLargePath);
        int count = 0;
        foreach (var _ in wb.StreamSheet("Worksheet").Take(10))
        {
            count++;
        }
        return count;
    }
}
