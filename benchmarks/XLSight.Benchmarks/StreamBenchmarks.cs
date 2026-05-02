using BenchmarkDotNet.Attributes;
using XLSight;

[MemoryDiagnoser]
[SimpleJob]
public class StreamBenchmarks
{
    private string _largePath = null!;
    private string _stringHeavyPath = null!;
    private string _xlLargePath = null!;

    [GlobalSetup]
    public void Setup()
    {
        _largePath       = Path.Combine(AppContext.BaseDirectory, "TestData", "large.xlsx");
        _stringHeavyPath = Path.Combine(AppContext.BaseDirectory, "TestData", "string_heavy.xlsx");
        _xlLargePath     = BenchmarkFixture.OptionalPath("xl_large.xlsx");
    }

    [Benchmark]
    public int StreamSheet_Large_AllRows() => ConsumeSafe(_largePath, "Numbers");

    [Benchmark]
    public int StreamSheet_Large_First10() => ConsumeSafe(_largePath, "Numbers", 10);

    [Benchmark]
    public int SheetReader_Large_AllRows() => ConsumeReader(_largePath, "Numbers");

    [Benchmark]
    public int SheetReader_Large_First10() => ConsumeReader(_largePath, "Numbers", 10);

    [Benchmark]
    public int StreamSheet_StringHeavy() => ConsumeSafe(_stringHeavyPath, "Strings");

    [Benchmark]
    public int SheetReader_StringHeavy() => ConsumeReader(_stringHeavyPath, "Strings");

    [Benchmark]
    public int StreamSheet_XlLarge_AllRows()
    {
        return ConsumeSafe(RequireXlLargePath(), "Worksheet");
    }

    [Benchmark]
    public int StreamSheet_XlLarge_First10()
    {
        return ConsumeSafe(RequireXlLargePath(), "Worksheet", 10);
    }

    [Benchmark]
    public int SheetReader_XlLarge_AllRows()
    {
        return ConsumeReader(RequireXlLargePath(), "Worksheet");
    }

    [Benchmark]
    public int SheetReader_XlLarge_First10()
    {
        return ConsumeReader(RequireXlLargePath(), "Worksheet", 10);
    }

    private string RequireXlLargePath() => BenchmarkFixture.RequireOptionalLargeFixture(_xlLargePath);

    private static int ConsumeSafe(string path, string sheet, int maxRows = int.MaxValue)
    {
        using var workbook = ExcelWorkbook.Open(path);
        int rows = 0;
        int cells = 0;
        foreach (var row in workbook.StreamSheet(sheet))
        {
            rows++;
            cells += row.Cells.Length;
            if (rows == maxRows)
            {
                break;
            }
        }

        return CombineCounts(rows, cells);
    }

    private static int ConsumeReader(string path, string sheet, int maxRows = int.MaxValue)
    {
        using var workbook = ExcelWorkbook.Open(path);
        using var reader = workbook.GetSheetReader(sheet);
        int rows = 0;
        int cells = 0;
        while (rows < maxRows && reader.Read())
        {
            rows++;
            cells += reader.Current.Cells.Length;
        }

        return CombineCounts(rows, cells);
    }

    private static int CombineCounts(int rows, int cells)
    {
        return unchecked((rows * 397) ^ cells);
    }
}
