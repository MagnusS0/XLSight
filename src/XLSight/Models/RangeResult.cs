namespace XLSight.Models;

/// <summary>Holds the result of reading a rectangular range of cells from an Excel worksheet.</summary>
public sealed class RangeResult
{
    /// <summary>Gets the name of the sheet from which this range was read.</summary>
    public required string Sheet { get; init; }

    /// <summary>Gets the 1-based row index of the top-left cell in this range.</summary>
    public required int StartRow { get; init; }

    /// <summary>Gets the 1-based column index of the top-left cell in this range.</summary>
    public required int StartColumn { get; init; }

    /// <summary>Gets the number of columns in this range.</summary>
    public required int Width { get; init; }

    /// <summary>Gets the number of rows in this range.</summary>
    public required int Height { get; init; }

    /// <summary>Gets the flat array of cell values in row-major order.</summary>
    public required ExcelCellValue[] Cells { get; init; }

    /// <summary>Access a cell by row/column offset (0-based) within the range.</summary>
    /// <param name="rowOffset">Zero-based row offset from the top of the range.</param>
    /// <param name="colOffset">Zero-based column offset from the left of the range.</param>
    /// <returns>A read-only reference to the cell value at the specified offset.</returns>
    public ref readonly ExcelCellValue this[int rowOffset, int colOffset]
        => ref Cells[(rowOffset * Width) + colOffset];

    /// <summary>Gets the total number of cells in this range.</summary>
    public int CellCount => Width * Height;
}
