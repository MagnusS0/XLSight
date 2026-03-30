namespace XLSight.Models.Analysis;

/// <summary>Describes formula density for a single worksheet column.</summary>
public sealed class FormulaColumnProfile
{
    /// <summary>Gets the 1-based worksheet column index.</summary>
    public required int ColumnIndex { get; init; }

    /// <summary>Gets the Excel-style column label.</summary>
    public required string ColumnLabel { get; init; }

    /// <summary>Gets the number of formula cells in the column.</summary>
    public required int FormulaCount { get; init; }
}
