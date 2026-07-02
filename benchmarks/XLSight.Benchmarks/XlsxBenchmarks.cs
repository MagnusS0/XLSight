using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Jobs;
using XLSight;

[MemoryDiagnoser]
[ShortRunJob]
public class XlsxBenchmarks
{
    private const string ComplexSheet = "Scenarios";
    private const string LargeSheet = "Numbers";
    private const string StringHeavySheet = "Strings";
    private const string XlLargeSheet = "Worksheet";

    private static readonly ExcelRange s_midRange = ExcelRange.Parse("B10:N20");

    private string _complexPath = null!;
    private string _largePath = null!;
    private string _stringHeavyPath = null!;
    private string _xlLargePath = null!;

    [GlobalSetup]
    public void Setup()
    {
        _complexPath = Path.Combine(AppContext.BaseDirectory, "TestData", "complex_workbook.xlsx");
        _largePath = Path.Combine(AppContext.BaseDirectory, "TestData", "large.xlsx");
        _stringHeavyPath = Path.Combine(AppContext.BaseDirectory, "TestData", "string_heavy.xlsx");
        _xlLargePath = BenchmarkFixture.OptionalPath("xl_large.xlsx");
    }

    [Benchmark(Description = "XLSX Open (100K)")]
    public int Open_Large()
    {
        using var workbook = ExcelWorkbook.Open(_largePath);
        return workbook.SheetNames.Count;
    }

    [Benchmark(Description = "XLSX ReadRange MidRange (complex)")]
    public int ReadRange_Complex_MidRange() => ConsumeRange(_complexPath, ComplexSheet, s_midRange);

    [Benchmark(Description = "XLSX StreamSheet AllRows (100K)")]
    public int StreamSheet_Large_AllRows() => ConsumeSafe(_largePath, LargeSheet);

    [Benchmark(Description = "XLSX StreamSheet First10 (100K)")]
    public int StreamSheet_Large_First10() => ConsumeSafe(_largePath, LargeSheet, 10);

    [Benchmark(Description = "XLSX SheetReader AllRows (100K)")]
    public int SheetReader_Large_AllRows() => ConsumeReader(_largePath, LargeSheet);

    [Benchmark(Description = "XLSX SheetReader First10 (100K)")]
    public int SheetReader_Large_First10() => ConsumeReader(_largePath, LargeSheet, 10);

    [Benchmark(Description = "XLSX StreamSheet StringHeavy")]
    public int StreamSheet_StringHeavy() => ConsumeSafe(_stringHeavyPath, StringHeavySheet);

    [Benchmark(Description = "XLSX SheetReader StringHeavy")]
    public int SheetReader_StringHeavy() => ConsumeReader(_stringHeavyPath, StringHeavySheet);

    [Benchmark(Description = "XLSX StreamSheet AllRows (1M)")]
    public int StreamSheet_XlLarge_AllRows() => ConsumeSafe(RequireXlLargePath(), XlLargeSheet);

    [Benchmark(Description = "XLSX StreamSheet First10 (1M)")]
    public int StreamSheet_XlLarge_First10() => ConsumeSafe(RequireXlLargePath(), XlLargeSheet, 10);

    [Benchmark(Description = "XLSX SheetReader AllRows (1M)")]
    public int SheetReader_XlLarge_AllRows() => ConsumeReader(RequireXlLargePath(), XlLargeSheet);

    [Benchmark(Description = "XLSX SheetReader First10 (1M)")]
    public int SheetReader_XlLarge_First10() => ConsumeReader(RequireXlLargePath(), XlLargeSheet, 10);

    private string RequireXlLargePath() => BenchmarkFixture.RequireOptionalLargeFixture(_xlLargePath);

    private static int ConsumeRange(string path, string sheet, ExcelRange range)
    {
        using var workbook = ExcelWorkbook.Open(path);
        RangeResult result = workbook.ReadRange(sheet, range);
        return BenchmarkFixture.CombineCounts(result.Height, result.Cells.Length);
    }

    private static int ConsumeSafe(string path, string sheet, int maxRows = int.MaxValue)
    {
        using var workbook = ExcelWorkbook.Open(path);
        int rows = 0;
        int cells = 0;
        foreach (ExcelRow row in workbook.StreamSheet(sheet))
        {
            rows++;
            cells += row.Cells.Length;
            if (rows == maxRows)
            {
                break;
            }
        }

        return BenchmarkFixture.CombineCounts(rows, cells);
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

        return BenchmarkFixture.CombineCounts(rows, cells);
    }

}
