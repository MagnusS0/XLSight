namespace XLSight.Models;

/// <summary>Holds the result of reading a single cell from an Excel worksheet.</summary>
public sealed class CellResult
{
    /// <summary>Gets the name of the sheet from which this cell was read.</summary>
    public required string Sheet { get; init; }

    /// <summary>Gets the 1-based row index of this cell.</summary>
    public required int Row { get; init; }

    /// <summary>Gets the 1-based column index of this cell.</summary>
    public required int Column { get; init; }

    /// <summary>Gets the decoded value of this cell.</summary>
    public required ExcelCellValue Value { get; init; }
}
