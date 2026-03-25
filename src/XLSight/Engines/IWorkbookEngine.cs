using XLSight.Models;
using XLSight.Models.Analysis;

namespace XLSight.Engines;

internal interface IWorkbookEngine : IDisposable, IAsyncDisposable
{
    public IReadOnlyList<string> SheetNames { get; }
    public bool IsDate1904 { get; }
    public bool HasMacros { get; }

    // Sync read — sheet name passed separately since ExcelRange/ExcelAddress have no Sheet field
    public ExcelCellResult ReadCell(string sheetName, ExcelAddress address, ExcelReadMode mode);
    public ExcelRangeResult ReadRange(string sheetName, ExcelRange range, ExcelReadMode mode);

    // Async read — true async comes in Phase 7; delegates to sync for now
    public Task<ExcelCellResult> ReadCellAsync(string sheetName, ExcelAddress address, ExcelReadMode mode, CancellationToken ct);
    public Task<ExcelRangeResult> ReadRangeAsync(string sheetName, ExcelRange range, ExcelReadMode mode, CancellationToken ct);

    // Analysis — implemented in Phase 5 (stubs throw NotSupportedException)
    public ExcelWorkbookInfo Analyze();
    public ExcelSheetInfo AnalyzeSheet(string sheetName);
    public Task<ExcelWorkbookInfo> AnalyzeAsync(CancellationToken ct);
    public Task<ExcelSheetInfo> AnalyzeSheetAsync(string sheetName, CancellationToken ct);

    // Streaming — implemented in Phase 6 (stubs throw NotSupportedException)
    public IEnumerable<ExcelRow> StreamRange(string sheetName, ExcelRange range, ExcelReadMode mode);
    public IAsyncEnumerable<ExcelRow> StreamRangeAsync(string sheetName, ExcelRange range, ExcelReadMode mode, CancellationToken ct);
}
