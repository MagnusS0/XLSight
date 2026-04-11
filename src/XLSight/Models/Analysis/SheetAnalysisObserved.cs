namespace XLSight.Analysis;

/// <summary>Observed worksheet facts collected by scanning the sheet contents.</summary>
public sealed class SheetAnalysisObserved
{
    internal static SheetAnalysisObserved Empty { get; } = new()
    {
        ValueUsedRange = null,
        StyledUsedRange = null,
        RowCount = 0,
        ColumnCount = 0,
        CellCount = 0,
        FormulaCount = 0,
        ArrayFormulaCount = 0,
        FormulaColumns = [],
        Columns = [],
    };

    /// <summary>Gets the bounding range of non-empty value cells, or null if no value cells exist.</summary>
    public required ExcelRange? ValueUsedRange { get; init; }

    /// <summary>Gets the bounding range of value cells and styled empty cells, or null if no relevant cells exist.</summary>
    public required ExcelRange? StyledUsedRange { get; init; }

    /// <summary>Gets the number of rows intersecting the observed value-used range.</summary>
    public required int RowCount { get; init; }

    /// <summary>Gets the number of columns intersecting the observed value-used range.</summary>
    public required int ColumnCount { get; init; }

    /// <summary>Gets the total number of non-empty cells in the sheet.</summary>
    public required int CellCount { get; init; }

    /// <summary>Gets the total number of formula cells in the sheet.</summary>
    public required int FormulaCount { get; init; }

    /// <summary>Gets the total number of array formula anchors in the sheet.</summary>
    public required int ArrayFormulaCount { get; init; }

    /// <summary>Gets formula counts by column.</summary>
    public required IReadOnlyList<FormulaColumnProfile> FormulaColumns { get; init; }

    /// <summary>Gets the column-level profiles for each column that contains data.</summary>
    public required IReadOnlyList<ColumnProfile> Columns { get; init; }
}
