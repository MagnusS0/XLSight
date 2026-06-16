using System.Buffers;
using XLSight.Internal.Metadata;

namespace XLSight.Internal.Readers.Xlsb;

internal sealed class XlsbSheetCursor(
    Stream worksheetStream,
    Lazy<XlsbSharedStringTable> sharedStrings,
    StyleTable styles,
    bool isDate1904,
    ReadMode mode,
    ExcelRange range,
    XlsbFormulaContext? formulaContext = null) : IRowCursor
{
    private const int UnboundedInputBufferSize = 1024 * 1024;

    private readonly XlsbRecordIterator _iterator = new(
        worksheetStream,
        range.IsUnbounded ? UnboundedInputBufferSize : XlsbRecordIterator.DefaultInputBufferSize);
    private readonly ExcelCellValue[] _cellPool = ArrayPool<ExcelCellValue>.Shared.Rent(ExcelLimits.MaxColumns);

    private ExcelRow _current;
    private int _currentStart;
    private int _currentWidth;
    private int _pendingRowIndex;
    private bool _hasPendingRow;
    private bool _done;
    private bool _disposed;

    public ExcelRow Current => _current;

    public bool IsSheetDone => _done;

    public bool MoveNext()
    {
        if (_done || _disposed) { return false; }

        ClearPreviousRow();

        return range.IsUnbounded ? MoveNextUnbounded() : MoveNextBounded();
    }

#pragma warning disable MA0051 // Keep the unbounded cell loop flat for the reader hot path.
    private bool MoveNextUnbounded()
    {
        int rowIndex = 0;
        int startColumn = ExcelLimits.MaxColumns + 1;
        int endColumn = 0;
        bool hasValue = false;

        if (_hasPendingRow)
        {
            rowIndex = _pendingRowIndex;
            _hasPendingRow = false;
        }

        while (_iterator.TryRead(out XlsbRecord record))
        {
            if ((uint)(record.Type - XlsbRecordType.BrtCellBlank) <=
                (uint)(XlsbRecordType.BrtFmlaError - XlsbRecordType.BrtCellBlank))
            {
                if (record.Type == XlsbRecordType.BrtCellBlank || rowIndex == 0)
                {
                    continue;
                }

                bool decoded = record.Type is
                    XlsbRecordType.BrtCellRk or XlsbRecordType.BrtCellReal or XlsbRecordType.BrtCellIsst
                    ? XlsbCellDecoder.TryDecodeCommonValue(
                        record, sharedStrings, styles, isDate1904, out int columnIndex, out ExcelCellValue value)
                    : XlsbCellDecoder.TryDecode(
                        record, sharedStrings, styles, isDate1904, mode, formulaContext, out columnIndex, out value);
                if (!decoded ||
                    !value.HasValue)
                {
                    continue;
                }

                _cellPool[columnIndex - 1] = value;
                startColumn = Math.Min(startColumn, columnIndex);
                endColumn = Math.Max(endColumn, columnIndex);
                hasValue = true;
                continue;
            }

            if (record.Type == XlsbRecordType.BrtEndSheetData)
            {
                return Complete(rowIndex, startColumn, endColumn, hasValue);
            }

            if (record.Type != XlsbRecordType.BrtRowHdr)
            {
                continue;
            }

            int nextRowIndex = XlsbBinary.ReadRowIndex(record.Payload);
            if (nextRowIndex <= 0)
            {
                continue;
            }

            if (hasValue)
            {
                _pendingRowIndex = nextRowIndex;
                _hasPendingRow = true;
                return YieldRow(rowIndex, startColumn, endColumn);
            }

            rowIndex = nextRowIndex;
        }

        return Complete(rowIndex, startColumn, endColumn, hasValue);
    }
#pragma warning restore MA0051

    private bool MoveNextBounded()
    {
        int rowIndex = 0;
        int startColumn = ExcelLimits.MaxColumns + 1;
        int endColumn = 0;
        bool hasValue = false;

        if (_hasPendingRow)
        {
            rowIndex = _pendingRowIndex;
            _hasPendingRow = false;
        }

        while (_iterator.TryRead(out XlsbRecord record))
        {
            // Fast path: cell records (BrtCellBlank=1 through BrtFmlaError=11) skip the
            // EndSheetData and RowHdr checks entirely.
            if ((uint)(record.Type - XlsbRecordType.BrtCellBlank) <=
                (uint)(XlsbRecordType.BrtFmlaError - XlsbRecordType.BrtCellBlank))
            {
                if (!TryHandleCellRecord(record, rowIndex, ref startColumn, ref endColumn, ref hasValue, out bool cellYielded))
                {
                    continue;
                }
                return cellYielded;
            }

            if (record.Type == XlsbRecordType.BrtEndSheetData) { return Complete(rowIndex, startColumn, endColumn, hasValue); }

            if (record.Type != XlsbRecordType.BrtRowHdr)
            {
                continue;
            }

            int nextRowIndex = XlsbBinary.ReadRowIndex(record.Payload);
            if (nextRowIndex <= 0)
            {
                continue;
            }

            if (hasValue)
            {
                _pendingRowIndex = nextRowIndex;
                _hasPendingRow = true;
                return YieldRow(rowIndex, startColumn, endColumn);
            }

            rowIndex = nextRowIndex;
            if (IsPastRange(rowIndex))
            {
                _done = true;
                return false;
            }
        }

        return Complete(rowIndex, startColumn, endColumn, hasValue);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _done = true;
        ClearPreviousRow();
        _iterator.Dispose();
        ArrayPool<ExcelCellValue>.Shared.Return(_cellPool, clearArray: false);
    }

    public bool TryParseNext(out ExcelRow row)
    {
        row = default;
        if (!MoveNext())
        {
            return false;
        }

        row = Current;
        return true;
    }

    public ValueTask<bool> RefillAsync(CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        return ValueTask.FromResult(false);
    }

    internal XlsbSheetCursor GetEnumerator() => this;

    private bool TryHandleCellRecord(
        XlsbRecord record,
        int rowIndex,
        ref int startColumn,
        ref int endColumn,
        ref bool hasValue,
        out bool yielded)
    {
        yielded = false;
        if (record.Type == XlsbRecordType.BrtCellBlank)
        {
            return false;
        }

        if (rowIndex == 0)
        {
            return false;
        }

        if (!XlsbCellDecoder.TryReadCellLocation(record.Payload, out int encodedColumn))
        {
            return false;
        }

        if (!ShouldDecodeCell(rowIndex, encodedColumn))
        {
            if (!IsPastRange(rowIndex)) { return false; }

            _done = true;
            yielded = hasValue && YieldRow(rowIndex, startColumn, endColumn);
            return true;
        }

        if (!XlsbCellDecoder.TryDecode(record, sharedStrings, styles, isDate1904, mode, formulaContext,
                out int columnIndex, out ExcelCellValue value) || !value.HasValue)
        {
            return false;
        }

        StoreDecodedCell(columnIndex, value, ref startColumn, ref endColumn, ref hasValue);
        return false;
    }

    private void StoreDecodedCell(
        int columnIndex,
        ExcelCellValue value,
        ref int startColumn,
        ref int endColumn,
        ref bool hasValue)
    {
        _cellPool[columnIndex - 1] = value;
        startColumn = Math.Min(startColumn, columnIndex);
        endColumn = Math.Max(endColumn, columnIndex);
        hasValue = true;
    }

    private bool ShouldDecodeCell(int rowIndex, int columnIndex) =>
        rowIndex >= range.TopLeft.Row &&
        rowIndex <= range.BottomRight.Row &&
        columnIndex >= range.TopLeft.Column &&
        columnIndex <= range.BottomRight.Column;

    private bool IsPastRange(int rowIndex) =>
        rowIndex > range.BottomRight.Row;

    private bool Complete(int rowIndex, int startColumn, int endColumn, bool hasValue)
    {
        _done = true;
        return hasValue && YieldRow(rowIndex, startColumn, endColumn);
    }

    private bool YieldRow(int rowIndex, int startColumn, int endColumn)
    {
        if (rowIndex <= 0 || startColumn > endColumn)
        {
            return false;
        }

        int width = endColumn - startColumn + 1;
        int start = startColumn - 1;
        _currentStart = start;
        _currentWidth = width;
        // ExcelRow.GetCell uses (column - startColumn) as the index into the memory slice,
        // so passing the slice at [start..start+width) avoids a redundant CopyTo.
        _current = new ExcelRow(rowIndex, _cellPool.AsMemory(start, width), startColumn);
        return true;
    }

    private void ClearPreviousRow()
    {
        if (_currentWidth > 0)
        {
            _cellPool.AsSpan(_currentStart, _currentWidth).Clear();
            _currentStart = 0;
            _currentWidth = 0;
        }
    }
}
