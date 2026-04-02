using XLSight.Models;
using XLSight.Models.Analysis;

namespace XLSight.Internal.Readers;

internal interface IWorkbookReader : IDisposable, IAsyncDisposable
{
    public bool IsFileBacked { get; }
    public IReadOnlyList<string> SheetNames { get; }
    public bool IsDate1904 { get; }
    public bool HasMacros { get; }

    // Sync read — sheet name passed separately since ExcelRange/ExcelAddress have no Sheet field
    public CellResult ReadCell(string sheetName, ExcelAddress address, ReadMode mode);
    public RangeResult ReadRange(string sheetName, ExcelRange range, ReadMode mode);

    public Task<CellResult> ReadCellAsync(string sheetName, ExcelAddress address, ReadMode mode, CancellationToken ct);
    public Task<RangeResult> ReadRangeAsync(string sheetName, ExcelRange range, ReadMode mode, CancellationToken ct);

    // Analysis — implemented in Phase 5 (stubs throw NotSupportedException)
    public WorkbookInfo Analyze(AnalysisLevel level, int maxDegreeOfParallelism = -1);
    public SheetInfo AnalyzeSheet(string sheetName, AnalysisLevel level);
    public Task<WorkbookInfo> AnalyzeAsync(AnalysisLevel level, int maxDegreeOfParallelism = -1, CancellationToken ct = default);
    public Task<SheetInfo> AnalyzeSheetAsync(string sheetName, AnalysisLevel level, CancellationToken ct);

    // Streaming — implemented in Phase 6 (stubs throw NotSupportedException)
    public IEnumerable<ExcelRow> StreamRange(string sheetName, ExcelRange range, ReadMode mode);
    public IAsyncEnumerable<ExcelRow> StreamRangeAsync(string sheetName, ExcelRange range, ReadMode mode, CancellationToken ct);
}
