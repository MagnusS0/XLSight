namespace XLSight.Models;

public sealed class ExcelRangeResult
{
    public required string Sheet { get; init; }
    public required int StartRow { get; init; }     // 1-based
    public required int StartColumn { get; init; }  // 1-based
    public required int Width { get; init; }
    public required int Height { get; init; }
    public required ExcelCellValue[] Cells { get; init; }

    /// <summary>Access a cell by row/column offset (0-based) within the range.</summary>
    public ref readonly ExcelCellValue this[int rowOffset, int colOffset]
        => ref Cells[(rowOffset * Width) + colOffset];

    public int CellCount => Width * Height;
}
