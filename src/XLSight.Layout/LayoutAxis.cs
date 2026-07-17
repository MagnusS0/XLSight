namespace XLSight.Layout;

/// <summary>Identifies labels or coordinates that explain one or more measure fields.</summary>
public sealed class LayoutAxis
{
    /// <summary>Gets the unique identifier of this axis within this analysis result.</summary>
    public required string Id { get; init; }

    /// <summary>Gets the inferred name of this axis (e.g. "WACC" beside a numeric coordinate
    /// column, or "CAGR (%)" captioning a horizontal header row), or null when none was detected.
    /// Horizontal axes are always probed for a title regardless of value kind, since a block
    /// caption may sit above a text or mixed-kind header row. Vertical axes are only named when
    /// they carry no self-describing labels of their own (Numeric or Date); a vertical text axis
    /// already identifies itself through <see cref="Samples"/>.</summary>
    public string? Title { get; init; }

    /// <summary>Gets whether the axis labels rows or columns.</summary>
    public required LayoutAxisOrientation Orientation { get; init; }

    /// <summary>Gets the dominant value kind carried by this axis.</summary>
    public required LayoutAxisValueKind ValueKind { get; init; }

    /// <summary>Gets the axis role relative to attached measure fields.</summary>
    public required LayoutAxisRole Role { get; init; }

    /// <summary>Gets the worksheet range occupied by the axis.</summary>
    public required ExcelRange Range { get; init; }

    /// <summary>Gets the number of measure rows or columns explained by this axis.</summary>
    public int Coverage => Orientation == LayoutAxisOrientation.Vertical ? Range.Height : Range.Width;

    /// <summary>Gets capped display samples from the axis for diagnostics.</summary>
    public required IReadOnlyList<string> Samples { get; init; }

    /// <summary>Gets inferred titled sections scoping runs of this axis (e.g. "Total Funding"
    /// heading the label rows beneath it); empty when no section structure was detected.</summary>
    public IReadOnlyList<LayoutAxisSection> Sections { get; init; } = [];
}
