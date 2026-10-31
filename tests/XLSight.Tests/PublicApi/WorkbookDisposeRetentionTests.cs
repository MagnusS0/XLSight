using System.Reflection;
using System.Runtime.CompilerServices;
using XLSight.Analysis;
using XLSight.Internal.Readers;
using XLSight.Internal.Readers.Xlsb;
using XLSight.Internal.Readers.Xlsx;
using Xunit;

namespace XLSight.Tests.PublicApi;

public sealed class WorkbookDisposeRetentionTests
{
    private const string Sheet = "Calculator";

    [Theory]
    [InlineData("complex_workbook.xlsx")]
    [InlineData("complex_workbook.xlsb")]
    public void Dispose_ReferencedWorkbook_ReleasesSharedStringsAndAnalyzerMetadata(string fileName)
    {
        var workbook = ExcelWorkbook.Open(GetTestDataPath(fileName));
        (WeakReference sharedStrings, WeakReference metadata) = Populate(workbook);

        workbook.Dispose();

        (object? strings, object? builtMetadata) = GetCachedState(GetEngine(workbook));
        Assert.Null(strings);
        Assert.Null(builtMetadata);

        CollectFully();
        Assert.False(sharedStrings.IsAlive, "Disposed workbook still retains its shared-string table.");
        Assert.False(metadata.IsAlive, "Disposed workbook still retains its analyzer metadata.");
        GC.KeepAlive(workbook);
    }

    [Fact]
    public async Task DisposeAsync_ReferencedWorkbook_ReleasesSharedStrings()
    {
        var workbook = ExcelWorkbook.Open(GetTestDataPath("complex_workbook.xlsx"));
        (WeakReference sharedStrings, _) = Populate(workbook);

        await workbook.DisposeAsync();

        CollectFully();
        Assert.False(sharedStrings.IsAlive);
        Assert.Throws<ObjectDisposedException>(() => workbook.ReadCell(Sheet, "A1"));
        GC.KeepAlive(workbook);
    }

    // XLSB is opened from a stream only: file-backed XLSB cursors read through the workbook's
    // shared archive, which Dispose closes (existing behaviour, unchanged).
    [Theory]
    [InlineData("complex_workbook.xlsx", false)]
    [InlineData("complex_workbook.xlsx", true)]
    [InlineData("complex_workbook.xlsb", true)]
    public void Reader_OpenedBeforeWorkbookDispose_KeepsReturningParsedSharedStrings(string fileName, bool fromStream)
    {
        string path = GetTestDataPath(fileName);
        List<string> expected;
        using (var reference = ExcelWorkbook.Open(path))
        {
            expected = ReadAllText(reference, disposeWorkbookAfterRows: -1);
        }

        var workbook = fromStream
            ? ExcelWorkbook.Open(new MemoryStream(File.ReadAllBytes(path)))
            : ExcelWorkbook.Open(path);

        // The first pass parses every shared string the sheet references; the second reader
        // must keep resolving them through its own table reference once the workbook is gone.
        Assert.Equal(expected, ReadAllText(workbook, disposeWorkbookAfterRows: -1));
        List<string> actual = ReadAllText(workbook, disposeWorkbookAfterRows: 1);

        Assert.Contains("Retirement Savings Calculator", expected);
        Assert.Equal(expected, actual);
        Assert.Throws<ObjectDisposedException>(() => workbook.ReadCell(Sheet, "A1"));
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static (WeakReference SharedStrings, WeakReference Metadata) Populate(ExcelWorkbook workbook)
    {
        Assert.NotEmpty(ReadAllText(workbook, disposeWorkbookAfterRows: -1));
        _ = workbook.Analyze(AnalysisLevel.Observed, maxDegreeOfParallelism: 1);
        (object? strings, object? metadata) = GetCachedState(GetEngine(workbook));
        Assert.NotNull(strings);
        Assert.NotNull(metadata);
        return (new WeakReference(strings), new WeakReference(metadata));
    }

    private static List<string> ReadAllText(ExcelWorkbook workbook, int disposeWorkbookAfterRows)
    {
        var values = new List<string>();
        using var reader = workbook.GetSheetReader(Sheet);
        int rows = 0;
        while (reader.Read())
        {
            ExcelRow row = reader.Current;
            for (int column = 1; column <= 16; column++)
            {
                ExcelCellValue cell = row.GetCell(column);
                if (cell.CellType == CellType.Text)
                {
                    values.Add(cell.AsText());
                }
            }

            if (++rows == disposeWorkbookAfterRows)
            {
                workbook.Dispose();
            }
        }

        return values;
    }

    private static void CollectFully()
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
    }

    private static IWorkbookReader GetEngine(ExcelWorkbook workbook) =>
        (IWorkbookReader)typeof(ExcelWorkbook)
            .GetField("_engine", BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(workbook)!;

    private static (object? SharedStrings, object? Metadata) GetCachedState(IWorkbookReader engine) => engine switch
    {
        XlsxWorkbookReader xlsx => (xlsx.CreatedSharedStrings, xlsx.BuiltAnalyzerMetadata),
        XlsbWorkbookReader xlsb => (xlsb.CreatedSharedStrings, xlsb.BuiltAnalyzerMetadata),
        _ => throw new InvalidOperationException(engine.GetType().Name),
    };

    private static string GetTestDataPath(string fileName) =>
        Path.Combine(AppContext.BaseDirectory, "TestData", fileName);
}
