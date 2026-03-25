using System.Runtime.InteropServices;

namespace XLSight.Models;

/// <summary>Represents a single row of cell values returned by a streaming read operation.</summary>
[StructLayout(LayoutKind.Auto)]
public readonly struct ExcelRow
{
    private readonly int _rowIndex;
    private readonly ReadOnlyMemory<ExcelCellValue> _cells;
    private readonly int _columnOffset;

    internal ExcelRow(int rowIndex, ReadOnlyMemory<ExcelCellValue> cells, int columnOffset = 1)
    {
        _rowIndex = rowIndex;
        _cells = cells;
        _columnOffset = columnOffset;
    }

    /// <summary>Gets the 1-based row index within the worksheet.</summary>
    public int RowIndex => _rowIndex;

    /// <summary>Gets the number of cells stored in this row.</summary>
    public int CellCount => _cells.Length;

    /// <summary>Gets the 1-based column index of the first cell in this row.</summary>
    public int StartColumn => _columnOffset;

    /// <summary>
    /// Get cell value by 1-based column index.
    /// Returns <see cref="ExcelCellValue.Empty"/> if the column is outside the stored range.
    /// </summary>
    /// <param name="columnIndex">The 1-based column index to retrieve.</param>
    /// <returns>The cell value, or <see cref="ExcelCellValue.Empty"/> if out of range.</returns>
    public ExcelCellValue GetCell(int columnIndex)
    {
        int offset = columnIndex - _columnOffset;
        if ((uint)offset >= (uint)_cells.Length)
        {
            return ExcelCellValue.Empty;
        }

        return _cells.Span[offset];
    }

    /// <summary>Span access for performance-sensitive consumers.</summary>
    public ReadOnlySpan<ExcelCellValue> Cells => _cells.Span;
}
