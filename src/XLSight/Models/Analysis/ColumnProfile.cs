namespace XLSight.Analysis;

/// <summary>Describes the statistical profile of a single column within an analyzed worksheet.</summary>
public sealed class ColumnProfile
{
    /// <summary>Gets the 1-based column index within the worksheet.</summary>
    public required int ColumnIndex { get; init; }

    /// <summary>Gets the inferred header label for this column, or null if none was detected.</summary>
    public required string? InferredHeader { get; init; }

    /// <summary>Gets the most frequently occurring cell type in this column.</summary>
    public required CellType DominantType { get; init; }

    /// <summary>Gets the count of non-empty cells in this column.</summary>
    public required int NonEmptyCount { get; init; }

    /// <summary>Gets the count of text cells in this column.</summary>
    public required int TextCount { get; init; }

    /// <summary>Gets the count of numeric cells in this column.</summary>
    public required int NumberCount { get; init; }

    /// <summary>Gets the count of date cells in this column.</summary>
    public required int DateCount { get; init; }

    /// <summary>Gets the count of boolean cells in this column.</summary>
    public required int BooleanCount { get; init; }

    /// <summary>Gets an estimate of the number of distinct values in this column.</summary>
    public required int DistinctValueEstimate { get; init; }

    /// <summary>
    /// Gets the exact distinct values in this column when their count does not exceed
    /// <see cref="AnalysisOptions.DistinctValuesCap"/>, or null for higher-cardinality columns
    /// (use <see cref="DistinctValueEstimate"/> instead). Values are grouped by type
    /// (text, number, date, boolean) and sorted within each group.
    /// </summary>
    public IReadOnlyList<string>? DistinctValues { get; init; }

    /// <summary>Gets the minimum numeric value found in this column, or null if no numeric cells exist.</summary>
    public required double? MinNumericValue { get; init; }

    /// <summary>Gets the maximum numeric value found in this column, or null if no numeric cells exist.</summary>
    public required double? MaxNumericValue { get; init; }

    /// <summary>Gets the length of the longest text value in this column, or null if no text cells exist.</summary>
    public required int? MaxTextLength { get; init; }

    /// <summary>Gets a value indicating whether any cells in this column contain formulas.</summary>
    public required bool HasFormulas { get; init; }
}
