using System.Text;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Jobs;
using ExcelDataReader;
using MiniExcelLibs;
using MiniExcelLibs.OpenXml;
using XLSight;

// Fairness notes:
//   XLSight StreamSheet decodes all cell values per row (default behaviour).
//   ExcelDataReader: reader.Read() alone skips value decoding; GetValue(i) added
//     for all columns so work is equivalent to XLSight.
//   MiniExcel: useHeaderRow:false skips header detection; FillMergedCells:false
//     is default but made explicit; EnableSharedStringCache:false forces the SST
//     fully in-memory (same model as all other libraries) — the default true would
//     disk-cache large SSTs and add per-lookup seek overhead.
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
    private string _largePath    = null!;
    private string? _xlLargePath;
    private int _largeColumns;
    private int _xlLargeColumns;

    private static readonly OpenXmlConfiguration s_cfg = new() { FillMergedCells = false, EnableSharedStringCache = false };

    [GlobalSetup]
    public void Setup()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        _largePath   = Path.Combine(AppContext.BaseDirectory, "TestData", "large.xlsx");
        var xlLarge  = Path.Combine(AppContext.BaseDirectory, "TestData", "xl_large.xlsx");
        _xlLargePath = File.Exists(xlLarge) ? xlLarge : null;

        using (var s = File.OpenRead(_largePath))
        using (var r = ExcelReaderFactory.CreateReader(s))
        {
            if (r.Read()) _largeColumns = r.FieldCount;
        }

        if (_xlLargePath is not null)
        {
            using var s = File.OpenRead(_xlLargePath);
            using var r = ExcelReaderFactory.CreateReader(s);
            NavigateToSheet(r, "Worksheet");
            if (r.Read()) { _xlLargeColumns = r.FieldCount; }
        }
    }

    // ── 100K-row file ────────────────────────────────────────────────────────

    [Benchmark(Baseline = true, Description = "XLSight AllRows (100K)")]
    public int XLSight_Large_AllRows()
    {
        using var wb = ExcelWorkbook.Open(_largePath);
        int n = 0;
        foreach (var _ in wb.StreamSheet("Numbers"))
        {
            n++;
        }
        return n;
    }

    [Benchmark(Description = "XLSight First10 (100K)")]
    public int XLSight_Large_First10()
    {
        using var wb = ExcelWorkbook.Open(_largePath);
        int n = 0;
        foreach (var _ in wb.StreamSheet("Numbers").Take(10))
        {
            n++;
        }
        return n;
    }

    [Benchmark(Description = "MiniExcel AllRows (100K)")]
    public int MiniExcel_Large_AllRows()
    {
        int n = 0;
        foreach (var _ in MiniExcel.Query(_largePath, useHeaderRow: false, configuration: s_cfg))
        {
            n++;
        }
        return n;
    }

    [Benchmark(Description = "MiniExcel First10 (100K)")]
    public int MiniExcel_Large_First10()
    {
        int n = 0;
        foreach (var _ in MiniExcel.Query(_largePath, useHeaderRow: false, configuration: s_cfg).Take(10))
        {
            n++;
        }
        return n;
    }

    [Benchmark(Description = "ExcelDataReader AllRows (100K)")]
    public int ExcelDataReader_Large_AllRows()
    {
        using var stream = File.Open(_largePath, FileMode.Open, FileAccess.Read, FileShare.Read);
        using var reader = ExcelReaderFactory.CreateReader(stream);
        int n = 0;
        while (reader.Read())
        {
            for (int i = 0; i < _largeColumns; i++) _ = reader.GetValue(i);
            n++;
        }
        return n;
    }

    [Benchmark(Description = "ExcelDataReader First10 (100K)")]
    public int ExcelDataReader_Large_First10()
    {
        using var stream = File.Open(_largePath, FileMode.Open, FileAccess.Read, FileShare.Read);
        using var reader = ExcelReaderFactory.CreateReader(stream);
        int n = 0;
        while (reader.Read() && n < 10)
        {
            for (int i = 0; i < _largeColumns; i++) _ = reader.GetValue(i);
            n++;
        }
        return n;
    }

    // ── ~1M-row file (skipped if xl_large.xlsx not present) ─────────────────

    [Benchmark(Description = "XLSight AllRows (1M)")]
    public int XLSight_XlLarge_AllRows()
    {
        if (_xlLargePath is null) return -1;
        using var wb = ExcelWorkbook.Open(_xlLargePath);
        int n = 0;
        foreach (var _ in wb.StreamSheet("Worksheet"))
        {
            n++;
        }
        return n;
    }

    [Benchmark(Description = "XLSight First10 (1M)")]
    public int XLSight_XlLarge_First10()
    {
        if (_xlLargePath is null) return -1;
        using var wb = ExcelWorkbook.Open(_xlLargePath);
        int n = 0;
        foreach (var _ in wb.StreamSheet("Worksheet").Take(10))
        {
            n++;
        }
        return n;
    }

    [Benchmark(Description = "MiniExcel AllRows (1M)")]
    public int MiniExcel_XlLarge_AllRows()
    {
        if (_xlLargePath is null) { return -1; }
        int n = 0;
        foreach (var _ in MiniExcel.Query(_xlLargePath, useHeaderRow: false, sheetName: "Worksheet", configuration: s_cfg))
        {
            n++;
        }
        return n;
    }

    [Benchmark(Description = "MiniExcel First10 (1M)")]
    public int MiniExcel_XlLarge_First10()
    {
        if (_xlLargePath is null) { return -1; }
        int n = 0;
        foreach (var _ in MiniExcel.Query(_xlLargePath, useHeaderRow: false, sheetName: "Worksheet", configuration: s_cfg).Take(10))
        {
            n++;
        }
        return n;
    }

    [Benchmark(Description = "ExcelDataReader AllRows (1M)")]
    public int ExcelDataReader_XlLarge_AllRows()
    {
        if (_xlLargePath is null) { return -1; }
        using var stream = File.Open(_xlLargePath!, FileMode.Open, FileAccess.Read, FileShare.Read);
        using var reader = ExcelReaderFactory.CreateReader(stream);
        NavigateToSheet(reader, "Worksheet");
        int n = 0;
        while (reader.Read())
        {
            for (int i = 0; i < _xlLargeColumns; i++) { _ = reader.GetValue(i); }
            n++;
        }
        return n;
    }

    [Benchmark(Description = "ExcelDataReader First10 (1M)")]
    public int ExcelDataReader_XlLarge_First10()
    {
        if (_xlLargePath is null) { return -1; }
        using var stream = File.Open(_xlLargePath!, FileMode.Open, FileAccess.Read, FileShare.Read);
        using var reader = ExcelReaderFactory.CreateReader(stream);
        NavigateToSheet(reader, "Worksheet");
        int n = 0;
        while (reader.Read() && n < 10)
        {
            for (int i = 0; i < _xlLargeColumns; i++) { _ = reader.GetValue(i); }
            n++;
        }
        return n;
    }

    private static void NavigateToSheet(IExcelDataReader reader, string sheetName)
    {
        do
        {
            if (string.Equals(reader.Name, sheetName, StringComparison.OrdinalIgnoreCase)) { return; }
        }
        while (reader.NextResult());
    }
}
