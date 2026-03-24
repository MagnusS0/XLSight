using System.Runtime.InteropServices;

namespace XLSight.Models;

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

    public int RowIndex => _rowIndex;
    public int CellCount => _cells.Length;
    public int StartColumn => _columnOffset;

    /// <summary>
    /// Get cell value by 1-based column index.
    /// Returns <see cref="ExcelCellValue.Empty"/> if the column is outside the stored range.
    /// </summary>
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
