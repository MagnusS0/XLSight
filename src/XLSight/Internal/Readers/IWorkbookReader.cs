using XLSight.Analysis;

namespace XLSight.Internal.Readers;

internal interface IWorkbookReader : IDisposable, IAsyncDisposable
{
    public bool IsFileBacked { get; }
    public WorkbookFormat Format { get; }
    public IReadOnlyList<string> SheetNames { get; }
    public bool IsDate1904 { get; }
    public bool HasMacros { get; }
    public VbaProjectInfo? GetVbaProject();
    public string GetVbaModuleSource(string moduleName);
    public byte[] GetVbaModuleSourceBytes(string moduleName);

    public ExcelCellValue ReadCell(string sheetName, ExcelAddress address, ReadMode mode);
    public RangeResult ReadRange(string sheetName, ExcelRange range, ReadMode mode);

    public Task<ExcelCellValue> ReadCellAsync(string sheetName, ExcelAddress address, ReadMode mode, CancellationToken ct);
    public Task<RangeResult> ReadRangeAsync(string sheetName, ExcelRange range, ReadMode mode, CancellationToken ct);

    public WorkbookInfo Analyze(AnalysisLevel level, int maxDegreeOfParallelism = -1);
    public SheetInfo AnalyzeSheet(string sheetName, AnalysisLevel level);
    public Task<WorkbookInfo> AnalyzeAsync(AnalysisLevel level, int maxDegreeOfParallelism = -1, CancellationToken ct = default);
    public Task<SheetInfo> AnalyzeSheetAsync(string sheetName, AnalysisLevel level, CancellationToken ct);

    public IRowCursor OpenCursor(string sheetName, ExcelRange range, ReadMode mode);
}
