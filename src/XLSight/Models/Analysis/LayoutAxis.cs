namespace XLSight.Analysis;

/// <summary>Identifies labels or coordinates that explain one or more measure fields.</summary>
public sealed class LayoutAxis
{
    /// <summary>Gets the unique identifier of this axis within this analysis result.</summary>
    public required string Id { get; init; }

    /// <summary>Gets whether the axis labels rows or columns.</summary>
    public required LayoutAxisOrientation Orientation { get; init; }

    /// <summary>Gets the dominant value kind carried by this axis.</summary>
    public required LayoutAxisValueKind ValueKind { get; init; }

    /// <summary>Gets the axis role relative to attached measure fields.</summary>
    public required LayoutAxisRole Role { get; init; }

    /// <summary>Gets the worksheet range occupied by the axis.</summary>
    public required ExcelRange Range { get; init; }

    /// <summary>Gets the number of measure rows or columns explained by this axis.</summary>
    public required int Coverage { get; init; }

    /// <summary>Gets capped display samples from the axis for diagnostics.</summary>
    public required IReadOnlyList<string> Samples { get; init; }

    /// <summary>Gets inferred titled sections scoping runs of this axis (e.g. "Total Funding"
    /// heading the label rows beneath it); empty when no section structure was detected.</summary>
    public IReadOnlyList<LayoutAxisSection> Sections { get; init; } = [];
}
