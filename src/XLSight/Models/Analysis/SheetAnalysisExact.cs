namespace XLSight.Analysis;

/// <summary>Exact worksheet facts parsed from worksheet XML and related package parts.</summary>
public sealed class SheetAnalysisExact
{
    /// <summary>Gets the declared worksheet dimension from the XML, or null if absent or invalid.</summary>
    public required ExcelRange? DeclaredDimension { get; init; }

    /// <summary>Gets all merged cell regions in this sheet.</summary>
    public required IReadOnlyList<MergedRegion> MergedRegions { get; init; }

    /// <summary>Gets the structured tables defined in this sheet.</summary>
    public required IReadOnlyList<TableInfo> Tables { get; init; }

    /// <summary>Gets the pivot tables defined in this sheet.</summary>
    public required IReadOnlyList<PivotTableInfo> PivotTables { get; init; }

    /// <summary>Gets the charts anchored to this sheet.</summary>
    public required IReadOnlyList<ChartInfo> Charts { get; init; }

    /// <summary>Gets the number of conditional-formatting blocks defined in the sheet XML.</summary>
    public required int ConditionalFormattingCount { get; init; }

    /// <summary>Gets the number of data validation rules defined in the sheet XML.</summary>
    public required int DataValidationCount { get; init; }

    /// <summary>Gets the number of hyperlinks defined in the sheet XML.</summary>
    public required int HyperlinkCount { get; init; }

    /// <summary>Gets the number of comments defined for this sheet.</summary>
    public required int CommentCount { get; init; }

    /// <summary>Gets the number of drawing anchors attached to this sheet.</summary>
    public required int DrawingCount { get; init; }
}
