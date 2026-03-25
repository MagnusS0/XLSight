namespace XLSight.Models.Analysis;

/// <summary>Describes the structural properties of a single Excel worksheet after analysis.</summary>
public sealed class ExcelSheetInfo
{
    /// <summary>Gets the name of the sheet.</summary>
    public required string SheetName { get; init; }

    /// <summary>Gets the 0-based index of this sheet within the workbook.</summary>
    public required int SheetIndex { get; init; }

    /// <summary>Gets the bounding range of all non-empty cells, or null if the sheet is empty.</summary>
    public required ExcelRange? UsedRange { get; init; }

    /// <summary>Gets the number of non-empty rows in the used range.</summary>
    public required int RowCount { get; init; }

    /// <summary>Gets the number of non-empty columns in the used range.</summary>
    public required int ColumnCount { get; init; }

    /// <summary>Gets the total number of non-empty cells in the sheet.</summary>
    public required int CellCount { get; init; }

    /// <summary>Gets the column-level profiles for each column that contains data.</summary>
    public required IReadOnlyList<ExcelColumnProfile> Columns { get; init; }

    /// <summary>Gets the Excel-style column letters (e.g. "A", "BC") of columns that contain formulas.</summary>
    public required IReadOnlyList<string> FormulaColumns { get; init; }

    /// <summary>Gets all merged cell regions in this sheet.</summary>
    public required IReadOnlyList<ExcelMergedRegion> MergedRegions { get; init; }

    /// <summary>Gets the structured tables defined in this sheet.</summary>
    public required IReadOnlyList<ExcelTableInfo> Tables { get; init; }

    /// <summary>Gets the 1-based row index inferred as the header row, or 0 if none could be inferred.</summary>
    public required int InferredHeaderRowIndex { get; init; }

    /// <summary>Gets a value indicating whether the sheet contains no data cells.</summary>
    public required bool IsEmpty { get; init; }
}
