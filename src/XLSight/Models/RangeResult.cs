using System.Runtime.InteropServices;

namespace XLSight;

/// <summary>Holds the result of reading a rectangular range of cells from an Excel worksheet.</summary>
public sealed class RangeResult
{
    private ExcelCellValue[] _buffer = [];
    private ExcelRow[]? _rows;

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

    /// <summary>Gets the flat cell buffer in row-major order as read-only memory.</summary>
    public required ReadOnlyMemory<ExcelCellValue> Cells
    {
        get => _buffer;
        init
        {
            if (MemoryMarshal.TryGetArray(value, out ArraySegment<ExcelCellValue> segment) &&
                segment.Array is { } array &&
                segment.Offset == 0 &&
                segment.Count == array.Length)
            {
                _buffer = array;
                return;
            }

            _buffer = value.ToArray();
        }
    }

    /// <summary>Access a cell by row/column offset (0-based) within the range.</summary>
    /// <param name="rowOffset">Zero-based row offset from the top of the range.</param>
    /// <param name="colOffset">Zero-based column offset from the left of the range.</param>
    /// <returns>A read-only reference to the cell value at the specified offset.</returns>
    public ref readonly ExcelCellValue this[int rowOffset, int colOffset]
        => ref _buffer[(rowOffset * Width) + colOffset];

    /// <summary>Gets the total number of cells in this range.</summary>
    public int CellCount => Width * Height;

    /// <summary>
    /// Returns the cells in this range as a list of <see cref="ExcelRow"/> values,
    /// one per row in the range.
    /// </summary>
    public IReadOnlyList<ExcelRow> Rows
    {
        get
        {
            return _rows ??= CreateRows();
        }
    }

    private ExcelRow[] CreateRows()
    {
        var rows = new ExcelRow[Height];
        for (int r = 0; r < Height; r++)
        {
            rows[r] = new ExcelRow(StartRow + r, new ReadOnlyMemory<ExcelCellValue>(_buffer, r * Width, Width), StartColumn);
        }

        return rows;
    }
}
