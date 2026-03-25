namespace XLSight.Models.Analysis;

public sealed class ExcelSheetInfo
{
    public required string SheetName { get; init; }
    public required int SheetIndex { get; init; }
    public required ExcelRange? UsedRange { get; init; }
    public required int RowCount { get; init; }
    public required int ColumnCount { get; init; }
    public required int CellCount { get; init; }
    public required IReadOnlyList<ExcelColumnProfile> Columns { get; init; }
    public required IReadOnlyList<string> FormulaColumns { get; init; }
    public required IReadOnlyList<ExcelMergedRegion> MergedRegions { get; init; }
    public required IReadOnlyList<ExcelTableInfo> Tables { get; init; }
    public required int InferredHeaderRowIndex { get; init; }
    public required bool IsEmpty { get; init; }
}
