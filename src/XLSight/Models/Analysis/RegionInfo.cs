namespace XLSight.Analysis;

/// <summary>Represents an inferred coherent region within a worksheet.</summary>
public sealed class RegionInfo
{
    /// <summary>Gets the region kind inferred from observed structure.</summary>
    public required RegionKind Kind { get; init; }

    /// <summary>Gets the region bounds.</summary>
    public required ExcelRange Range { get; init; }

    /// <summary>Gets the number of non-empty cells in the region.</summary>
    public required int CellCount { get; init; }

    /// <summary>Gets the number of rows in the region.</summary>
    public required int RowCount { get; init; }

    /// <summary>Gets the number of columns in the region.</summary>
    public required int ColumnCount { get; init; }

    /// <summary>Gets the number of formula cells observed in the region.</summary>
    public required int FormulaCount { get; init; }

    /// <summary>Gets the 1-based row indices identified as header rows within the region.</summary>
    public required IReadOnlyList<int> HeaderRows { get; init; }

    /// <summary>Gets a coarse confidence score from 0 to 1 for the inferred kind.</summary>
    public required double Confidence { get; init; }

    /// <summary>Gets compact evidence strings supporting the inference.</summary>
    public required IReadOnlyList<string> Evidence { get; init; }
}
