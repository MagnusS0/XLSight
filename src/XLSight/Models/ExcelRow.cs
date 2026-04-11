using System.Collections;
using System.Runtime.InteropServices;

namespace XLSight;

/// <summary>Represents a single row of cell values returned by a streaming read operation.</summary>
[StructLayout(LayoutKind.Auto)]
public readonly struct ExcelRow : IEnumerable<ExcelCellValue>
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

    /// <summary>
    /// Returns a read-only reference to a cell by 1-based column index, avoiding a 24-byte struct copy.
    /// Falls back to a reference to <see cref="ExcelCellValue.Empty"/> when the column is out of range.
    /// </summary>
    /// <param name="columnIndex">The 1-based column index to retrieve.</param>
    public ref readonly ExcelCellValue GetCellRef(int columnIndex)
    {
        int offset = columnIndex - _columnOffset;
        if ((uint)offset < (uint)_cells.Length)
        {
            return ref _cells.Span[offset];
        }

        return ref ExcelCellValue.Empty;
    }

    /// <summary>Span access for performance-sensitive consumers.</summary>
    public ReadOnlySpan<ExcelCellValue> Cells => _cells.Span;

    /// <summary>
    /// Returns a zero-allocation struct enumerator for use in <c>foreach</c> loops.
    /// </summary>
    public Enumerator GetEnumerator() => new(_cells);

    /// <inheritdoc/>
    IEnumerator<ExcelCellValue> IEnumerable<ExcelCellValue>.GetEnumerator() =>
        MemoryMarshal.ToEnumerable(_cells).GetEnumerator();

    /// <inheritdoc/>
    IEnumerator IEnumerable.GetEnumerator() =>
        MemoryMarshal.ToEnumerable(_cells).GetEnumerator();

    /// <summary>Zero-allocation struct enumerator over the row's cells.</summary>
    [StructLayout(LayoutKind.Auto)]
    public struct Enumerator
    {
        private readonly ReadOnlyMemory<ExcelCellValue> _cells;
        private int _index;

        internal Enumerator(ReadOnlyMemory<ExcelCellValue> cells)
        {
            _cells = cells;
            _index = -1;
        }

        /// <summary>Advances to the next cell.</summary>
        public bool MoveNext() => ++_index < _cells.Length;

        /// <summary>Gets the cell at the current position.</summary>
        public ExcelCellValue Current => _cells.Span[_index];
    }

    /// <summary>
    /// Returns a new <see cref="ExcelRow"/> whose cells are copied into an independent array.
    /// Use this when storing rows beyond the current iteration — the streaming cursor reuses
    /// a single pooled buffer, so calling <c>ToSnapshot()</c> produces a stable, independent copy.
    /// </summary>
    public ExcelRow ToSnapshot()
    {
        var copy = new ExcelCellValue[_cells.Length];
        _cells.Span.CopyTo(copy);
        return new ExcelRow(_rowIndex, copy, _columnOffset);
    }
}
