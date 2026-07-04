namespace XLSight.Analysis;

/// <summary>Ties together measure fields that share at least one axis, and the axes they share.</summary>
public sealed class LayoutGroupInfo
{
    /// <summary>Gets the unique identifier of this group within this analysis result.</summary>
    public required string Id { get; init; }

    /// <summary>Gets the inferred title found just above the group (e.g. "Summary of balance
    /// sheet, end of period (DKKm)"), or null when none was detected.</summary>
    public string? Title { get; init; }

    /// <summary>Gets the worksheet range covered by this group.</summary>
    public required ExcelRange Range { get; init; }

    /// <summary>Gets the identifiers of axes in this group.</summary>
    public required IReadOnlyList<string> AxisIds { get; init; }

    /// <summary>Gets the identifiers of measure fields in this group.</summary>
    public required IReadOnlyList<string> MeasureFieldIds { get; init; }
}
