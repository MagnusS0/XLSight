using XLSight.Internal.Metadata;
using System.Buffers;
using XLSight.Models;

namespace XLSight.Internal.Readers.Xlsx;

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
    /// <see langword="true"/> when the sheet data has been fully consumed (the
    /// <c>&lt;/sheetData&gt;</c> end-tag was found or the stream was exhausted).
    /// Used by the async streaming loop to break early without extra I/O.
    /// </summary>
    internal bool IsSheetDone => _done;

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

    /// <summary>
    /// Try to parse the next row from the already-buffered data WITHOUT doing any I/O.
    /// Returns <see langword="true"/> if a complete row was produced from buffered data.
    /// Returns <see langword="false"/> if more data is needed (call
    /// <see cref="RefillAsync"/> then retry) or the sheet is exhausted
    /// (check <see cref="IsSheetDone"/>).
    /// </summary>
    /// <remarks>
    /// <para>
    /// Implements the inner-sync half of the outer-async / inner-sync streaming loop.
    /// The buffer's <see cref="ScanBuffer.NoIO"/> flag is set for the duration of the
    /// <see cref="MoveNext"/> call so that any synchronous <see cref="ScanBuffer.Refill"/>
    /// call inside the scanner is a no-op.  If a refill was skipped (<see cref="ScanBuffer.IOSkipped"/>),
    /// the buffer start pointer is restored via <see cref="ScanBuffer.RewindTo"/> so that
    /// <see cref="RefillAsync"/> can compact from the original position and reload data.
    /// </para>
    /// <para>
    /// Rows whose XML spans a buffer boundary (extremely rare with the 64 KB buffer) will
    /// trigger a roll-back and retry after the next async refill — they are never skipped
    /// or partially yielded.
    /// </para>
    /// </remarks>
    internal bool TryParseNext(out ExcelRow row)
    {
        row = default;
        if (_done) { return false; }

        // Save cursor state so we can roll back cleanly if a synchronous I/O attempt is
        // skipped.  Because NoIO prevents compaction, _start is unchanged by the attempted
        // parse and this saved value is always valid for RewindTo.
        int savedStart   = _buf.Start;
        int savedLastRow = _lastRow;

        _buf.IOSkipped = false;
        _buf.NoIO      = true;
        bool result    = MoveNext();
        _buf.NoIO      = false;

        if (result && !_buf.IOSkipped)
        {
            // Complete row decoded entirely from buffered data — safe to yield.
            row = _current;
            return true;
        }

        // If sync I/O was skipped, roll back and let the outer async loop refill.
        // If I/O was not skipped and MoveNext returned false, keep _done as-is so
        // the async loop can terminate instead of spinning forever.
        if (_buf.IOSkipped)
        {
            _buf.RewindTo(savedStart);
            _lastRow = savedLastRow;
            _done = false;
        }

        return false;
    }

    /// <summary>Asynchronously refills the underlying <see cref="ScanBuffer"/>.</summary>
    internal ValueTask<bool> RefillAsync(CancellationToken ct = default) =>
        _buf.RefillAsync(ct);

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
