namespace XLSight.Models.Analysis;

// Phase 5 — will be extended with sheet profiles, named ranges, and column analysis.
public sealed class ExcelWorkbookInfo
{
    public required IReadOnlyList<ExcelSheetInfo> Sheets { get; init; }
    public required bool HasMacros { get; init; }
    public required bool IsDate1904 { get; init; }
}
