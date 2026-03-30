namespace XLSight.Models.Analysis;

/// <summary>Describes a pivot table defined within a workbook.</summary>
public sealed class PivotTableInfo
{
    /// <summary>Gets the pivot table name.</summary>
    public required string Name { get; init; }

    /// <summary>Gets the sheet containing the pivot table.</summary>
    public required string Sheet { get; init; }

    /// <summary>Gets the pivot table destination range, if one could be parsed.</summary>
    public required ExcelRange? Range { get; init; }

    /// <summary>Gets the source reference backing the pivot table, if one could be resolved.</summary>
    public required string? SourceReference { get; init; }
}
