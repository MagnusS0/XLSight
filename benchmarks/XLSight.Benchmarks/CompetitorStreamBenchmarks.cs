using System.Text;
using System.Collections.Generic;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Jobs;
using ExcelDataReader;
using MiniExcelLibs;
using MiniExcelLibs.OpenXml;
using XLSight;

// Fairness notes:
//   XLSight GetSheetReader is the fastest supported public forward-only API and
//     still decodes all cell values per row before exposing Current.
//   XLSight StreamSheet is also benchmarked separately to show the cost of the
//     safe snapshotting enumerable contract.
//   ExcelDataReader: reader.Read() alone skips value decoding; GetValue(i) added
//     for all columns so work is equivalent to XLSight row decoding.
//   MiniExcel: useHeaderRow:false skips header detection; FillMergedCells:false
//     is default but made explicit; EnableSharedStringCache:false forces the SST
//     fully in-memory (same model as all other libraries) — the default true would
//     disk-cache large SSTs and add per-lookup seek overhead.
//
// Sink contract:
//   All benchmarks consume both row count and cell count.
//   The returned int is a deterministic token derived from both so all libraries
//   are forced through equivalent row/cell workloads.
//
// Bounded range selection (complex_workbook.xlsx):
//   Scenarios!B10:N20 is a centered 11x13 slice from the scenario table.
//   XLSight uses ReadRange for the bounded rectangular read, while competitors
//   consume the same effective rows/cells from that rectangle.
//
// Sheet selection (xl_large.xlsx):
//   xl_large.xlsx has 4 sheets. "Worksheet" is the 4th sheet (~985K rows).
//   All three libraries are directed to the same "Worksheet" sheet by name so
//   the row count is identical. EDR navigates via NextResult(); MiniExcel uses
//   the sheetName parameter; XLSight uses StreamSheet("Worksheet").
[MemoryDiagnoser]
[ShortRunJob]
public class CompetitorStreamBenchmarks
{
    private const string ComplexSheet = "Scenarios";
    private const string XlLargeSheet = "Worksheet";
    private const string MidRangeAddress = "B10:N20";

    private string _complexPath   = null!;
    private string _largePath    = null!;
    private string _xlLargePath  = null!;
    private int _largeColumns;

    private static readonly ExcelRange s_midRange = ExcelRange.Parse(MidRangeAddress);
    private static readonly string[] s_midRangeColumns =
        CreateColumnKeys(s_midRange.TopLeft.Column, s_midRange.BottomRight.Column);
    private static readonly OpenXmlConfiguration s_cfg = new() { FillMergedCells = false, EnableSharedStringCache = false };

    [GlobalSetup]
    public void Setup()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        _complexPath = Path.Combine(AppContext.BaseDirectory, "TestData", "complex_workbook.xlsx");
        _largePath   = Path.Combine(AppContext.BaseDirectory, "TestData", "large.xlsx");
        _xlLargePath = BenchmarkFixture.OptionalPath("xl_large.xlsx");

        using (var s = File.OpenRead(_largePath))
        using (var r = ExcelReaderFactory.CreateReader(s))
        {
            if (r.Read()) _largeColumns = r.FieldCount;
        }
    }

    // ── Mid-sheet rectangular range (complex workbook) ───────────────────────

    [Benchmark(Description = "XLSight ReadRange MidRange (complex)")]
    public int XLSight_Complex_MidRange()
    {
        return ConsumeXlsightRange(_complexPath, ComplexSheet, s_midRange);
    }

    [Benchmark(Description = "MiniExcel MidRange (complex)")]
    public int MiniExcel_Complex_MidRange()
    {
        return ConsumeMiniExcelRange(_complexPath, ComplexSheet, s_midRange, s_midRangeColumns);
    }

    [Benchmark(Description = "ExcelDataReader MidRange (complex)")]
    public int ExcelDataReader_Complex_MidRange()
    {
        return ConsumeExcelDataReaderRange(_complexPath, ComplexSheet, s_midRange);
    }

    // ── 100K-row file ────────────────────────────────────────────────────────

    [Benchmark(Baseline = true, Description = "XLSight reader AllRows (100K)")]
    public int XLSightReader_Large_AllRows()
    {
        return ConsumeXlsightReader(_largePath, "Numbers");
    }

    [Benchmark(Description = "XLSight reader First10 (100K)")]
    public int XLSightReader_Large_First10()
    {
        return ConsumeXlsightReader(_largePath, "Numbers", 10);
    }

    [Benchmark(Description = "XLSight safe AllRows (100K)")]
    public int XLSightSafe_Large_AllRows()
    {
        return ConsumeXlsightSafe(_largePath, "Numbers");
    }

    [Benchmark(Description = "XLSight safe First10 (100K)")]
    public int XLSightSafe_Large_First10()
    {
        return ConsumeXlsightSafe(_largePath, "Numbers", 10);
    }

    [Benchmark(Description = "MiniExcel AllRows (100K)")]
    public int MiniExcel_Large_AllRows()
    {
        return ConsumeMiniExcel(_largePath);
    }

    [Benchmark(Description = "MiniExcel First10 (100K)")]
    public int MiniExcel_Large_First10()
    {
        return ConsumeMiniExcel(_largePath, maxRows: 10);
    }

    [Benchmark(Description = "ExcelDataReader AllRows (100K)")]
    public int ExcelDataReader_Large_AllRows()
    {
        using var stream = File.Open(_largePath, FileMode.Open, FileAccess.Read, FileShare.Read);
        using var reader = ExcelReaderFactory.CreateReader(stream);
        int rows = 0;
        int cells = 0;
        while (reader.Read())
        {
            for (int i = 0; i < _largeColumns; i++)
            {
                _ = reader.GetValue(i);
                cells++;
            }

            rows++;
        }

        return CombineCounts(rows, cells);
    }

    [Benchmark(Description = "ExcelDataReader First10 (100K)")]
    public int ExcelDataReader_Large_First10()
    {
        using var stream = File.Open(_largePath, FileMode.Open, FileAccess.Read, FileShare.Read);
        using var reader = ExcelReaderFactory.CreateReader(stream);
        int rows = 0;
        int cells = 0;
        while (reader.Read() && rows < 10)
        {
            for (int i = 0; i < _largeColumns; i++)
            {
                _ = reader.GetValue(i);
                cells++;
            }

            rows++;
        }

        return CombineCounts(rows, cells);
    }

    // ── ~1M-row file (run explicitly when xl_large.xlsx is present) ─────────

    [Benchmark(Description = "XLSight reader AllRows (1M)")]
    public int XLSightReader_XlLarge_AllRows()
    {
        return ConsumeXlsightReader(RequireXlLargePath(), XlLargeSheet);
    }

    [Benchmark(Description = "XLSight reader First10 (1M)")]
    public int XLSightReader_XlLarge_First10()
    {
        return ConsumeXlsightReader(RequireXlLargePath(), XlLargeSheet, 10);
    }

    [Benchmark(Description = "XLSight safe AllRows (1M)")]
    public int XLSightSafe_XlLarge_AllRows()
    {
        return ConsumeXlsightSafe(RequireXlLargePath(), XlLargeSheet);
    }

    [Benchmark(Description = "XLSight safe First10 (1M)")]
    public int XLSightSafe_XlLarge_First10()
    {
        return ConsumeXlsightSafe(RequireXlLargePath(), XlLargeSheet, 10);
    }

    [Benchmark(Description = "MiniExcel AllRows (1M)")]
    public int MiniExcel_XlLarge_AllRows()
    {
        return ConsumeMiniExcel(RequireXlLargePath(), XlLargeSheet);
    }

    [Benchmark(Description = "MiniExcel First10 (1M)")]
    public int MiniExcel_XlLarge_First10()
    {
        return ConsumeMiniExcel(RequireXlLargePath(), XlLargeSheet, 10);
    }

    [Benchmark(Description = "ExcelDataReader AllRows (1M)")]
    public int ExcelDataReader_XlLarge_AllRows()
    {
        using var stream = File.Open(RequireXlLargePath(), FileMode.Open, FileAccess.Read, FileShare.Read);
        using var reader = ExcelReaderFactory.CreateReader(stream);
        NavigateToSheet(reader, XlLargeSheet);
        int rows = 0;
        int cells = 0;
        int columns = 0;
        while (reader.Read())
        {
            if (columns == 0)
            {
                columns = reader.FieldCount;
            }

            for (int i = 0; i < columns; i++)
            {
                _ = reader.GetValue(i);
                cells++;
            }

            rows++;
        }

        return CombineCounts(rows, cells);
    }

    [Benchmark(Description = "ExcelDataReader First10 (1M)")]
    public int ExcelDataReader_XlLarge_First10()
    {
        using var stream = File.Open(RequireXlLargePath(), FileMode.Open, FileAccess.Read, FileShare.Read);
        using var reader = ExcelReaderFactory.CreateReader(stream);
        NavigateToSheet(reader, XlLargeSheet);
        int rows = 0;
        int cells = 0;
        int columns = 0;
        while (reader.Read() && rows < 10)
        {
            if (columns == 0)
            {
                columns = reader.FieldCount;
            }

            for (int i = 0; i < columns; i++)
            {
                _ = reader.GetValue(i);
                cells++;
            }

            rows++;
        }

        return CombineCounts(rows, cells);
    }

    private string RequireXlLargePath() => BenchmarkFixture.RequireOptionalLargeFixture(_xlLargePath);

    private static void NavigateToSheet(IExcelDataReader reader, string sheetName)
    {
        do
        {
            if (string.Equals(reader.Name, sheetName, StringComparison.OrdinalIgnoreCase)) { return; }
        }
        while (reader.NextResult());
    }

    private static int ConsumeXlsightSafe(string path, string sheet, int maxRows = int.MaxValue)
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

    private static int ConsumeXlsightReader(string path, string sheet, int maxRows = int.MaxValue)
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

    private static int ConsumeXlsightRange(string path, string sheet, ExcelRange range)
    {
        using var workbook = ExcelWorkbook.Open(path);
        RangeResult result = workbook.ReadRange(sheet, range);
        int cells = 0;
        foreach (var value in result.Cells.Span)
        {
            _ = value;
            cells++;
        }

        return CombineCounts(result.Height, cells);
    }

    private static int ConsumeMiniExcel(string path, string? sheetName = null, int maxRows = int.MaxValue)
    {
        int rows = 0;
        int cells = 0;
        foreach (IDictionary<string, object?> row in MiniExcel.Query(
                     path,
                     useHeaderRow: false,
                     sheetName: sheetName,
                     configuration: s_cfg).Cast<IDictionary<string, object?>>())
        {
            rows++;
            foreach (var value in row.Values)
            {
                _ = value;
                cells++;
            }

            if (rows == maxRows)
            {
                break;
            }
        }

        return CombineCounts(rows, cells);
    }

    private static int ConsumeMiniExcelRange(
        string path,
        string sheetName,
        ExcelRange range,
        IReadOnlyList<string> columns)
    {
        int rows = 0;
        int cells = 0;
        int rowIndex = 0;

        foreach (IDictionary<string, object?> row in MiniExcel.Query(
                     path,
                     useHeaderRow: false,
                     sheetName: sheetName,
                     configuration: s_cfg).Cast<IDictionary<string, object?>>())
        {
            rowIndex++;
            if (rowIndex < range.TopLeft.Row)
            {
                continue;
            }

            if (rowIndex > range.BottomRight.Row)
            {
                break;
            }

            rows++;
            foreach (string column in columns)
            {
                row.TryGetValue(column, out object? value);
                _ = value;
                cells++;
            }
        }

        return CombineCounts(rows, cells);
    }

    private static int ConsumeExcelDataReaderRange(string path, string sheetName, ExcelRange range)
    {
        using var stream = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        using var reader = ExcelReaderFactory.CreateReader(stream);
        NavigateToSheet(reader, sheetName);
        int rows = 0;
        int cells = 0;
        int rowIndex = 0;

        while (reader.Read())
        {
            rowIndex++;
            if (rowIndex < range.TopLeft.Row)
            {
                continue;
            }

            if (rowIndex > range.BottomRight.Row)
            {
                break;
            }

            rows++;
            for (int columnIndex = range.TopLeft.Column - 1; columnIndex < range.BottomRight.Column; columnIndex++)
            {
                _ = columnIndex < reader.FieldCount ? reader.GetValue(columnIndex) : null;
                cells++;
            }
        }

        return CombineCounts(rows, cells);
    }

    private static string[] CreateColumnKeys(int startColumn, int endColumn)
    {
        var columns = new string[endColumn - startColumn + 1];
        for (int columnIndex = startColumn; columnIndex <= endColumn; columnIndex++)
        {
            columns[columnIndex - startColumn] = new ExcelAddress(columnIndex, 1).ToString()[..^1];
        }

        return columns;
    }

    private static int CombineCounts(int rows, int cells)
    {
        return unchecked((rows * 397) ^ cells);
    }
}
