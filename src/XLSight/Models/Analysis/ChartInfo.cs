namespace XLSight.Analysis;

/// <summary>Describes a chart defined in the workbook.</summary>
public sealed class ChartInfo
{
    /// <summary>Gets the display title, if one could be extracted.</summary>
    public required string? Title { get; init; }

    /// <summary>Gets the sheet containing the drawing anchor for this chart.</summary>
    public required string Sheet { get; init; }

    /// <summary>Gets the chart part path inside the workbook package.</summary>
    public required string PartPath { get; init; }

    /// <summary>Gets the source references used by chart series.</summary>
    public required IReadOnlyList<string> SourceReferences { get; init; }
}
