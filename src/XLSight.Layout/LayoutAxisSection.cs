namespace XLSight.Layout;

/// <summary>A titled run of rows or columns inside an inferred axis, e.g. a section header like
/// "Total Funding" scoping the label rows beneath it until the next section begins.</summary>
public sealed class LayoutAxisSection
{
    /// <summary>Gets the section title text, capped at the analyzer's sample length.</summary>
    public required string Title { get; init; }

    /// <summary>Gets the axis cells this section scopes, starting at the section header itself.</summary>
    public required ExcelRange Range { get; init; }
}
