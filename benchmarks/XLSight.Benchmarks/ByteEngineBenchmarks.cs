using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Jobs;
using XLSight;
using XLSight.Internal.Readers.Xlsx;
using XLSight.Internal.Packaging;
using XLSight.Models;
using XLSight.Internal.Metadata;

// Measures the ByteEngine (XlsxSheetScanner) in isolation and through the full public API.
//
// Group 1 (ScannerOnly_*): SST and styles are pre-loaded in GlobalSetup.
//   Each iteration re-opens the worksheet bytes from a MemoryStream snapshot,
//   eliminating I/O and ZIP decompression variance so only parser throughput is measured.
//
// Group 2 (FullPath_*): full file open per iteration — includes ZIP open,
//   decompression, SST/styles load, and scanning. Mirrors production code paths.
[MemoryDiagnoser]
[ShortRunJob]
public class ByteEngineBenchmarks
{
    private string _largePath = null!;
    private string? _xlLargePath;

    private SharedStringTable _largeSst = SharedStringTable.Empty;
    private StyleTable _largeStyles = StyleTable.Default;
    private byte[] _largeWsBytes = [];
    private bool _largeIsDate1904;

    private SharedStringTable _xlLargeSst = SharedStringTable.Empty;
    private StyleTable _xlLargeStyles = StyleTable.Default;
    private byte[] _xlLargeWsBytes = [];
    private bool _xlLargeIsDate1904;

    [GlobalSetup]
    public void Setup()
    {
        _largePath = Path.Combine(AppContext.BaseDirectory, "TestData", "large.xlsx");
        var xlLarge = Path.Combine(AppContext.BaseDirectory, "TestData", "xl_large.xlsx");
        _xlLargePath = File.Exists(xlLarge) ? xlLarge : null;

        (_largeSst, _largeStyles, _largeWsBytes, _largeIsDate1904) = LoadFixture(_largePath, "Numbers");
        if (_xlLargePath is not null)
        {
            (_xlLargeSst, _xlLargeStyles, _xlLargeWsBytes, _xlLargeIsDate1904) =
                LoadFixture(_xlLargePath, "Worksheet");
        }
    }

    // ── Group 1: Scanner-only (pre-loaded SST + styles, in-memory bytes) ────

    [Benchmark(Baseline = true, Description = "ByteEngine IEnumerable (100K)")]
    public int ScannerOnly_ByteEngine_Large_AllRows()
    {
        using var ms = new MemoryStream(_largeWsBytes, writable: false);
        int n = 0;
        foreach (var _ in XlsxSheetScanner.ScanRows(
            ms, _largeSst, _largeStyles, _largeIsDate1904,
            ReadMode.Values, ExcelRange.Unbounded))
        {
            n++;
        }
        return n;
    }

    [Benchmark(Description = "ByteEngine IEnumerable First10 (100K)")]
    public int ScannerOnly_ByteEngine_Large_First10()
    {
        using var ms = new MemoryStream(_largeWsBytes, writable: false);
        int n = 0;
        foreach (var _ in XlsxSheetScanner.ScanRows(
            ms, _largeSst, _largeStyles, _largeIsDate1904,
            ReadMode.Values, ExcelRange.Unbounded).Take(10))
        {
            n++;
        }
        return n;
    }

    [Benchmark(Description = "ByteEngine IEnumerable (1M)")]
    public int ScannerOnly_ByteEngine_XlLarge_AllRows()
    {
        if (_xlLargeWsBytes.Length == 0) { return -1; }
        using var ms = new MemoryStream(_xlLargeWsBytes, writable: false);
        int n = 0;
        foreach (var _ in XlsxSheetScanner.ScanRows(
            ms, _xlLargeSst, _xlLargeStyles, _xlLargeIsDate1904,
            ReadMode.Values, ExcelRange.Unbounded))
        {
            n++;
        }
        return n;
    }

    // ── Group 1b: Cursor (zero per-row allocation) ───────────────────────────

    [Benchmark(Description = "Cursor (100K)")]
    public int ScannerOnly_Cursor_Large_AllRows()
    {
        using var ms = new MemoryStream(_largeWsBytes, writable: false);
        using var cursor = XlsxSheetScanner.OpenCursor(
            ms, _largeSst, _largeStyles, _largeIsDate1904,
            ReadMode.Values, ExcelRange.Unbounded);
        int n = 0;
        while (cursor.MoveNext()) { n++; }
        return n;
    }

    [Benchmark(Description = "Cursor First10 (100K)")]
    public int ScannerOnly_Cursor_Large_First10()
    {
        using var ms = new MemoryStream(_largeWsBytes, writable: false);
        using var cursor = XlsxSheetScanner.OpenCursor(
            ms, _largeSst, _largeStyles, _largeIsDate1904,
            ReadMode.Values, ExcelRange.Unbounded);
        int n = 0;
        while (n < 10 && cursor.MoveNext()) { n++; }
        return n;
    }

    [Benchmark(Description = "Cursor (1M)")]
    public int ScannerOnly_Cursor_XlLarge_AllRows()
    {
        if (_xlLargeWsBytes.Length == 0) { return -1; }
        using var ms = new MemoryStream(_xlLargeWsBytes, writable: false);
        using var cursor = XlsxSheetScanner.OpenCursor(
            ms, _xlLargeSst, _xlLargeStyles, _xlLargeIsDate1904,
            ReadMode.Values, ExcelRange.Unbounded);
        int n = 0;
        while (cursor.MoveNext()) { n++; }
        return n;
    }

    [Benchmark(Description = "Cursor First10 (1M)")]
    public int ScannerOnly_Cursor_XlLarge_First10()
    {
        if (_xlLargeWsBytes.Length == 0) { return -1; }
        using var ms = new MemoryStream(_xlLargeWsBytes, writable: false);
        using var cursor = XlsxSheetScanner.OpenCursor(
            ms, _xlLargeSst, _xlLargeStyles, _xlLargeIsDate1904,
            ReadMode.Values, ExcelRange.Unbounded);
        int n = 0;
        while (n < 10 && cursor.MoveNext()) { n++; }
        return n;
    }

    // ── Group 2: Full path (file open per iteration) ─────────────────────────

    [Benchmark(Description = "XLSight full-path (100K)")]
    public int FullPath_XLSight_Large_AllRows()
    {
        using var wb = ExcelWorkbook.Open(_largePath);
        int n = 0;
        foreach (var _ in wb.StreamSheet("Numbers")) { n++; }
        return n;
    }

    [Benchmark(Description = "XLSight full-path (1M)")]
    public int FullPath_XLSight_XlLarge_AllRows()
    {
        if (_xlLargePath is null) { return -1; }
        using var wb = ExcelWorkbook.Open(_xlLargePath);
        int n = 0;
        foreach (var _ in wb.StreamSheet("Worksheet")) { n++; }
        return n;
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private (SharedStringTable sst, StyleTable styles, byte[] wsBytes, bool isDate1904)
        LoadFixture(string path, string sheetName)
    {
        using var package = XlsxPackage.Open(File.OpenRead(path), ownsStream: true);

        using var wbStream = package.GetEntry("xl/workbook.xml")!.OpenBuffered();
        using var relsStream = package.GetEntry("xl/_rels/workbook.xml.rels")!.OpenBuffered();
        var def = WorkbookParser.Parse(wbStream);
        var metadata = RelationshipsParser.Parse(relsStream, def);

        SharedStringTable sst = SharedStringTable.Empty;
        var sstEntry = package.GetEntry("xl/sharedStrings.xml");
        if (sstEntry is not null)
        {
            using var s = sstEntry.OpenBuffered();
            sst = SharedStringsParser.Parse(s);
        }

        StyleTable styles = StyleTable.Default;
        var stylesEntry = package.GetEntry("xl/styles.xml");
        if (stylesEntry is not null)
        {
            using var s = stylesEntry.OpenBuffered();
            styles = StylesParser.Parse(s);
        }

        var sheet = metadata.Sheets.First(sh => string.Equals(sh.Name, sheetName, StringComparison.Ordinal));
        var wsEntry = package.GetEntry(sheet.Path)!;
        using var wsStream = wsEntry.OpenBuffered();
        var wsBytes = ReadAllBytes(wsStream);

        return (sst, styles, wsBytes, metadata.UsesDate1904);
    }

    private static byte[] ReadAllBytes(Stream s)
    {
        using var ms = new MemoryStream();
        s.CopyTo(ms);
        return ms.ToArray();
    }
}
