using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Jobs;
using XLSight;
using XLSight.Internal.Metadata;
using XLSight.Internal.Packaging;
using XLSight.Internal.Readers.Xlsx;

// Measures the internal scanner/cursor in isolation and the public APIs through
// the full workbook path.
//
// Group 1 (ScannerOnly_*): SST and styles are pre-loaded in GlobalSetup. Each
// iteration re-opens worksheet bytes from a MemoryStream snapshot, eliminating
// file I/O and ZIP/package setup so parser throughput dominates.
//
// Group 2 (FullPath_*): opens the workbook from disk per iteration using public
// XLSight APIs. Both public row APIs are benchmarked:
//   - GetSheetReader(): fastest public forward-only borrowed reader API
//   - StreamSheet(): safe snapshotting enumerable API
[MemoryDiagnoser]
[ShortRunJob]
public class ByteEngineBenchmarks
{
    private string _largePath = null!;
    private string _xlLargePath = null!;

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
        _xlLargePath = BenchmarkFixture.OptionalPath("xl_large.xlsx");

        (_largeSst, _largeStyles, _largeWsBytes, _largeIsDate1904) = LoadFixture(_largePath, "Numbers");
        if (ShouldLoadXlLargeFixture())
        {
            (_xlLargeSst, _xlLargeStyles, _xlLargeWsBytes, _xlLargeIsDate1904) =
                LoadFixture(RequireXlLargePath(), "Worksheet");
        }
    }

    // ── Group 1: Scanner-only (internal microbenchmarks) ─────────────────────

    [Benchmark(Baseline = true, Description = "ByteEngine IEnumerable (100K)")]
    public int ScannerOnly_ByteEngine_Large_AllRows()
    {
        using var stream = new MemoryStream(_largeWsBytes, writable: false);
        int count = 0;
        foreach (var _ in XlsxSheetScanner.ScanRows(
            stream, _largeSst, _largeStyles, _largeIsDate1904,
            ReadMode.Values, ExcelRange.Unbounded))
        {
            count++;
        }

        return count;
    }

    [Benchmark(Description = "ByteEngine IEnumerable First10 (100K)")]
    public int ScannerOnly_ByteEngine_Large_First10()
    {
        using var stream = new MemoryStream(_largeWsBytes, writable: false);
        int count = 0;
        foreach (var _ in XlsxSheetScanner.ScanRows(
            stream, _largeSst, _largeStyles, _largeIsDate1904,
            ReadMode.Values, ExcelRange.Unbounded).Take(10))
        {
            count++;
        }

        return count;
    }

    [Benchmark(Description = "ByteEngine IEnumerable (1M)")]
    public int ScannerOnly_ByteEngine_XlLarge_AllRows()
    {
        EnsureXlLargeLoaded();
        using var stream = new MemoryStream(_xlLargeWsBytes, writable: false);
        int count = 0;
        foreach (var _ in XlsxSheetScanner.ScanRows(
            stream, _xlLargeSst, _xlLargeStyles, _xlLargeIsDate1904,
            ReadMode.Values, ExcelRange.Unbounded))
        {
            count++;
        }

        return count;
    }

    [Benchmark(Description = "Cursor (100K)")]
    public int ScannerOnly_Cursor_Large_AllRows()
    {
        using var stream = new MemoryStream(_largeWsBytes, writable: false);
        using var cursor = XlsxSheetScanner.OpenCursor(
            stream, _largeSst, _largeStyles, _largeIsDate1904,
            ReadMode.Values, ExcelRange.Unbounded);
        int count = 0;
        while (cursor.MoveNext())
        {
            count++;
        }

        return count;
    }

    [Benchmark(Description = "Cursor First10 (100K)")]
    public int ScannerOnly_Cursor_Large_First10()
    {
        using var stream = new MemoryStream(_largeWsBytes, writable: false);
        using var cursor = XlsxSheetScanner.OpenCursor(
            stream, _largeSst, _largeStyles, _largeIsDate1904,
            ReadMode.Values, ExcelRange.Unbounded);
        int count = 0;
        while (count < 10 && cursor.MoveNext())
        {
            count++;
        }

        return count;
    }

    [Benchmark(Description = "Cursor (1M)")]
    public int ScannerOnly_Cursor_XlLarge_AllRows()
    {
        EnsureXlLargeLoaded();
        using var stream = new MemoryStream(_xlLargeWsBytes, writable: false);
        using var cursor = XlsxSheetScanner.OpenCursor(
            stream, _xlLargeSst, _xlLargeStyles, _xlLargeIsDate1904,
            ReadMode.Values, ExcelRange.Unbounded);
        int count = 0;
        while (cursor.MoveNext())
        {
            count++;
        }

        return count;
    }

    [Benchmark(Description = "Cursor First10 (1M)")]
    public int ScannerOnly_Cursor_XlLarge_First10()
    {
        EnsureXlLargeLoaded();
        using var stream = new MemoryStream(_xlLargeWsBytes, writable: false);
        using var cursor = XlsxSheetScanner.OpenCursor(
            stream, _xlLargeSst, _xlLargeStyles, _xlLargeIsDate1904,
            ReadMode.Values, ExcelRange.Unbounded);
        int count = 0;
        while (count < 10 && cursor.MoveNext())
        {
            count++;
        }

        return count;
    }

    // ── Group 2: Full path (public APIs) ──────────────────────────────────────

    [Benchmark(Description = "XLSight reader full-path (100K)")]
    public int FullPath_XLSightReader_Large_AllRows() => ConsumeReader(_largePath, "Numbers");

    [Benchmark(Description = "XLSight safe full-path (100K)")]
    public int FullPath_XLSightStream_Large_AllRows() => ConsumeSafe(_largePath, "Numbers");

    [Benchmark(Description = "XLSight reader full-path (1M)")]
    public int FullPath_XLSightReader_XlLarge_AllRows()
        => ConsumeReader(RequireXlLargePath(), "Worksheet");

    [Benchmark(Description = "XLSight safe full-path (1M)")]
    public int FullPath_XLSightStream_XlLarge_AllRows()
        => ConsumeSafe(RequireXlLargePath(), "Worksheet");

    private void EnsureXlLargeLoaded()
    {
        if (_xlLargeWsBytes.Length == 0)
        {
            (_xlLargeSst, _xlLargeStyles, _xlLargeWsBytes, _xlLargeIsDate1904) =
                LoadFixture(RequireXlLargePath(), "Worksheet");
        }
    }

    private string RequireXlLargePath() => BenchmarkFixture.RequireOptionalLargeFixture(_xlLargePath);

    private static bool ShouldLoadXlLargeFixture() =>
        Environment.GetCommandLineArgs().Any(static arg => arg.Contains("XlLarge", StringComparison.Ordinal));

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

    private static int CombineCounts(int rows, int cells)
    {
        return unchecked((rows * 397) ^ cells);
    }

    private (SharedStringTable sst, StyleTable styles, byte[] wsBytes, bool isDate1904)
        LoadFixture(string path, string sheetName)
    {
        using var package = XlsxPackage.Open(File.OpenRead(path), ownsStream: true);

        using var workbookStream = package.GetEntry("xl/workbook.xml")!.OpenBuffered();
        using var relationshipsStream = package.GetEntry("xl/_rels/workbook.xml.rels")!.OpenBuffered();
        var definition = WorkbookParser.Parse(workbookStream);
        var metadata = RelationshipsParser.Parse(relationshipsStream, definition);

        SharedStringTable sharedStrings = SharedStringTable.Empty;
        var sharedStringsEntry = package.GetEntry("xl/sharedStrings.xml");
        if (sharedStringsEntry is not null)
        {
            using var stream = sharedStringsEntry.OpenBuffered();
            sharedStrings = SharedStringsParser.Parse(stream);
        }

        StyleTable styles = StyleTable.Default;
        var stylesEntry = package.GetEntry("xl/styles.xml");
        if (stylesEntry is not null)
        {
            using var stream = stylesEntry.OpenBuffered();
            styles = StylesParser.Parse(stream);
        }

        var sheet = metadata.Sheets.First(sh => string.Equals(sh.Name, sheetName, StringComparison.Ordinal));
        var worksheetEntry = package.GetEntry(sheet.Path)!;
        using var worksheetStream = worksheetEntry.OpenBuffered();
        var worksheetBytes = ReadAllBytes(worksheetStream);

        return (sharedStrings, styles, worksheetBytes, metadata.UsesDate1904);
    }

    private static byte[] ReadAllBytes(Stream stream)
    {
        using var buffer = new MemoryStream();
        stream.CopyTo(buffer);
        return buffer.ToArray();
    }
}
