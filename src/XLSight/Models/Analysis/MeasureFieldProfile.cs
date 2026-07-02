namespace XLSight.Analysis;

/// <summary>Describes statistics scoped to a single measure field.</summary>
public sealed class MeasureFieldProfile
{
    /// <summary>Gets the number of non-empty cells observed inside the measure field.</summary>
    public required int CellCount { get; init; }

    /// <summary>Gets the number of numeric cells observed inside the measure field.</summary>
    public required int NumericCount { get; init; }

    /// <summary>Gets the number of text cells observed inside the measure field.</summary>
    public required int TextCount { get; init; }

    /// <summary>Gets the number of formula cells observed inside the measure field.</summary>
    public required int FormulaCount { get; init; }

    /// <summary>Gets the minimum numeric value in the field, or null when no numeric values were observed.</summary>
    public required double? MinNumeric { get; init; }

    /// <summary>Gets the maximum numeric value in the field, or null when no numeric values were observed.</summary>
    public required double? MaxNumeric { get; init; }
}
