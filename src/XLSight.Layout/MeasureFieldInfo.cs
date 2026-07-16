namespace XLSight.Analysis.Layout;

/// <summary>Describes a value-bearing field attached to zero or more layout axes.</summary>
public sealed class MeasureFieldInfo
{
    /// <summary>Gets the unique identifier of this field within this analysis result.</summary>
    public required string Id { get; init; }

    /// <summary>Gets the worksheet range occupied by the measure field.</summary>
    public required ExcelRange Range { get; init; }

    /// <summary>Gets the identifiers of axes attached to this field.</summary>
    public required IReadOnlyList<string> AxisIds { get; init; }

    /// <summary>Gets the dimensional rank, equal to the number of attached axes.</summary>
    public int Rank => AxisIds.Count;

    /// <summary>Gets statistics scoped to the measure field cells.</summary>
    public required MeasureFieldProfile Profile { get; init; }
}
