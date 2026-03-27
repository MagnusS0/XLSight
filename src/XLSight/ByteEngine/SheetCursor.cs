using XLSight.SharedStrings;
using System.Buffers;
using XLSight.Models;
using XLSight.Styles;

namespace XLSight.ByteEngine;

/// <summary>
/// Zero-allocation forward cursor over worksheet rows.
/// A single <see cref="ExcelCellValue"/> buffer is rented once and reused for every row.
/// </summary>
/// <remarks>
/// <para>
/// <b>Contract:</b> <see cref="Current"/> is only valid until the next call to
/// <see cref="MoveNext"/>. Do not store <see cref="Current"/> or its
/// <see cref="ExcelRow.Cells"/> span across iterations — the backing buffer
/// is overwritten on each advance.
/// </para>
/// <para>
/// Supports duck-typed <c>foreach</c>: <c>foreach (var row in cursor)</c> is valid
/// as long as the loop body does not store the row reference beyond the loop body.
/// </para>
/// </remarks>
internal sealed class SheetCursor : IDisposable
{
    private readonly ScanBuffer _buf;
    private readonly SharedStringTable _sharedStrings;
    private readonly StyleTable _styles;
    private readonly bool _isDate1904;
    private readonly ExcelRange _range;
    private readonly ExcelCellValue[] _cellPool;

    private ExcelRow _current;
    private int _lastRow;
    private bool _done;
    private bool _disposed;

    internal SheetCursor(
        Stream entryStream,
        SharedStringTable sharedStrings,
        StyleTable styles,
        bool isDate1904,
        ExcelRange range,
        long seekHint)
    {
        _sharedStrings = sharedStrings;
        _styles = styles;
        _isDate1904 = isDate1904;
        _range = range;
        _cellPool = ArrayPool<ExcelCellValue>.Shared.Rent(ExcelLimits.MaxColumns);
        _buf = new ScanBuffer(entryStream);

        if (!XlsxSheetScanner.SeekToSheetData(_buf, entryStream, seekHint))
        {
            _done = true;
        }
    }

    /// <summary>
    /// The current row. Only valid until the next <see cref="MoveNext"/> call.
    /// </summary>
    public ExcelRow Current => _current;

    /// <summary>
    /// Advances to the next row. Returns <see langword="false"/> when the sheet is exhausted.
    /// </summary>
    public bool MoveNext()
    {
        if (_done) { return false; }

        // Clear the portion of the pool buffer used by the previous row.
        if (_current.CellCount > 0)
        {
            _cellPool.AsSpan(0, _current.CellCount).Clear();
        }

        while (true)
        {
            if (!XlsxSheetScanner.TryReadRowStart(_buf, ref _lastRow))
            {
                _done = true;
                return false;
            }

            int rowIndex = _lastRow;

            if (!_range.IsUnbounded && rowIndex > _range.BottomRight.Row)
            {
                _done = true;
                return false;
            }

            if (!_range.IsUnbounded && rowIndex < _range.TopLeft.Row)
            {
                XlsxSheetScanner.SkipToEndTag(_buf, XlsxSheetScanner.TagRow);
                continue;
            }

            if (XlsxSheetScanner.FillRowCells(
                _buf, rowIndex, _sharedStrings, _styles, _isDate1904, _range, _cellPool,
                out int startCol, out int width))
            {
                // ExcelRow wraps the shared pool memory — valid only until next MoveNext().
                _current = new ExcelRow(rowIndex, _cellPool.AsMemory(0, width), startCol);
                return true;
            }
        }
    }

    /// <summary>Enables duck-typed <c>foreach</c> over the cursor.</summary>
    public SheetCursor GetEnumerator() => this;

    /// <inheritdoc/>
    public void Dispose()
    {
        if (_disposed) { return; }
        _disposed = true;
        _done = true;
        ArrayPool<ExcelCellValue>.Shared.Return(_cellPool, clearArray: false);
        _buf.Dispose();
    }
}
