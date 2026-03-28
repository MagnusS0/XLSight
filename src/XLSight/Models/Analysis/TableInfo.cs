namespace XLSight.Models.Analysis;

/// <summary>Describes a structured table defined within an Excel worksheet.</summary>
public sealed class TableInfo
{
    /// <summary>Gets the display name of the table as defined in the workbook.</summary>
    public required string Name { get; init; }

    /// <summary>Gets the name of the sheet that contains this table.</summary>
    public required string Sheet { get; init; }

    /// <summary>Gets the bounding range of the table, including the header row.</summary>
    public required ExcelRange Range { get; init; }

    /// <summary>Gets the ordered list of column header names for this table.</summary>
    public required IReadOnlyList<string> ColumnNames { get; init; }
}
