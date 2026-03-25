namespace XLSight.Models.Analysis;

// Phase 5 — will be extended with column profiles, header inference, merged regions, and tables.
public sealed class ExcelSheetInfo
{
    public required string SheetName { get; init; }
    public required int SheetIndex { get; init; }
}
