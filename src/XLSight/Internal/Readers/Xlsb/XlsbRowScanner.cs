using XLSight.Internal.Metadata;

namespace XLSight.Internal.Readers.Xlsb;

internal sealed class XlsbRowScanner : IDisposable
{
    private readonly XlsbRecordIterator _iterator;
    private readonly Lazy<XlsbSharedStringTable> _sharedStrings;
    private readonly StyleTable _styles;
    private readonly bool _isDate1904;
    private readonly ReadMode _mode;
    private readonly ExcelRange _range;
    private readonly XlsbFormulaContext? _formulaContext;
    private readonly ExcelCellValue[] _cellPool;

    private ExcelRow _current;
    private int _currentWidth;
    private int _pendingRowIndex;
    private bool _hasPendingRow;
    private bool _done;
    private bool _disposed;

    internal XlsbRowScanner(
        XlsbRecordIterator iterator,
        Lazy<XlsbSharedStringTable> sharedStrings,
        StyleTable styles,
        bool isDate1904,
        ReadMode mode,
        ExcelRange range,
        ExcelCellValue[] cellPool,
        XlsbFormulaContext? formulaContext = null)
    {
        _iterator = iterator;
        _sharedStrings = sharedStrings;
        _styles = styles;
        _isDate1904 = isDate1904;
        _mode = mode;
        _range = range;
        _cellPool = cellPool;
        _formulaContext = formulaContext;
    }

    internal ExcelRow Current => _current;

    internal bool IsDone => _done;

    internal bool MoveNext()
    {
        if (_done || _disposed)
        {
            return false;
        }

        ClearPreviousRow();

        int rowIndex = 0;
        int startColumn = ExcelLimits.MaxColumns + 1;
        int endColumn = 0;
        bool hasValue = false;

        ConsumePendingRow(ref rowIndex);

        while (_iterator.TryRead(out XlsbRecord record))
        {
            if (record.Type == XlsbRecordType.BrtEndSheetData) { return Complete(rowIndex, startColumn, endColumn, hasValue); }
            if (record.Type == XlsbRecordType.BrtRowHdr
                && TryHandleRowHeader(record.Payload, ref rowIndex, startColumn, endColumn, hasValue, out bool yielded))
            {
                return yielded;
            }

            if (!TryHandleCellRecord(record, ref rowIndex, ref startColumn, ref endColumn, ref hasValue, out yielded))
            {
                continue;
            }

            return yielded;
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
    }

    private static bool IsSupportedCellRecord(int recordType) => recordType
        is XlsbRecordType.BrtCellRk
        or XlsbRecordType.BrtCellError
        or XlsbRecordType.BrtCellBool
        or XlsbRecordType.BrtCellReal
        or XlsbRecordType.BrtCellSt
        or XlsbRecordType.BrtCellIsst
        or XlsbRecordType.BrtFmlaString
        or XlsbRecordType.BrtFmlaNum
        or XlsbRecordType.BrtFmlaBool
        or XlsbRecordType.BrtFmlaError;

    private void ConsumePendingRow(ref int rowIndex)
    {
        if (!_hasPendingRow)
        {
            return;
        }

        rowIndex = _pendingRowIndex;
        _hasPendingRow = false;
    }

    private bool TryHandleRowHeader(
        ReadOnlySpan<byte> payload,
        ref int rowIndex,
        int startColumn,
        int endColumn,
        bool hasValue,
        out bool yielded)
    {
        yielded = false;
        int nextRowIndex = XlsbBinary.ReadRowIndex(payload);
        if (nextRowIndex <= 0)
        {
            return false;
        }

        if (hasValue)
        {
            SetPendingRow(nextRowIndex);
            yielded = YieldRow(rowIndex, startColumn, endColumn);
            return true;
        }

        rowIndex = nextRowIndex;
        if (IsPastRange(rowIndex))
        {
            _done = true;
            yielded = false;
            return true;
        }

        return false;
    }

    private bool TryHandleCellRecord(
        XlsbRecord record,
        ref int rowIndex,
        ref int startColumn,
        ref int endColumn,
        ref bool hasValue,
        out bool yielded)
    {
        yielded = false;
        if (record.Type == XlsbRecordType.BrtCellBlank || !IsSupportedCellRecord(record.Type))
        {
            return false;
        }

        if (rowIndex == 0 || !TryReadCellColumn(record, out int columnIndex))
        {
            return false;
        }

        if (!ShouldDecodeCell(rowIndex, columnIndex))
        {
            if (!IsPastRange(rowIndex))
            {
                return false;
            }

            _done = true;
            yielded = YieldIfPopulated(rowIndex, startColumn, endColumn, hasValue);
            return true;
        }

        if (!TryDecodeCell(record, rowIndex, out int cellRowIndex, out columnIndex, out ExcelCellValue value))
        {
            return false;
        }

        return IncludeDecodedCell(rowIndex, columnIndex, value, ref startColumn, ref endColumn, ref hasValue, out yielded);
    }

    private static bool TryReadCellColumn(XlsbRecord record, out int columnIndex) =>
        XlsbCellDecoder.TryReadCellLocation(record.Payload, out columnIndex);

    private bool TryDecodeCell(
        XlsbRecord record,
        int rowIndex,
        out int cellRowIndex,
        out int columnIndex,
        out ExcelCellValue value)
        => XlsbCellDecoder.TryDecode(
            record,
            rowIndex,
            _sharedStrings,
            _styles,
            _isDate1904,
            _mode,
            _formulaContext,
            out cellRowIndex,
            out columnIndex,
            out value);

    private bool IncludeDecodedCell(
        int rowIndex,
        int columnIndex,
        ExcelCellValue value,
        ref int startColumn,
        ref int endColumn,
        ref bool hasValue,
        out bool yielded)
    {
        yielded = false;
        if (!ShouldIncludeCell(rowIndex, columnIndex, value))
        {
            if (!IsPastRange(rowIndex))
            {
                return false;
            }

            _done = true;
            yielded = YieldIfPopulated(rowIndex, startColumn, endColumn, hasValue);
            return true;
        }

        _cellPool[columnIndex - 1] = value;
        startColumn = Math.Min(startColumn, columnIndex);
        endColumn = Math.Max(endColumn, columnIndex);
        hasValue = true;
        return false;
    }

    private void SetPendingRow(int rowIndex)
    {
        _pendingRowIndex = rowIndex;
        _hasPendingRow = true;
    }

    private bool ShouldIncludeCell(int rowIndex, int columnIndex, ExcelCellValue value)
    {
        if (!value.HasValue)
        {
            return false;
        }

        return ShouldDecodeCell(rowIndex, columnIndex);
    }

    private bool ShouldDecodeCell(int rowIndex, int columnIndex) =>
        _range.IsUnbounded ||
        rowIndex >= _range.TopLeft.Row &&
        rowIndex <= _range.BottomRight.Row &&
        columnIndex >= _range.TopLeft.Column &&
        columnIndex <= _range.BottomRight.Column;

    private bool IsPastRange(int rowIndex) =>
        !_range.IsUnbounded && rowIndex > _range.BottomRight.Row;

    private bool Complete(int rowIndex, int startColumn, int endColumn, bool hasValue)
    {
        _done = true;
        return YieldIfPopulated(rowIndex, startColumn, endColumn, hasValue);
    }

    private bool YieldIfPopulated(int rowIndex, int startColumn, int endColumn, bool hasValue) =>
        hasValue && YieldRow(rowIndex, startColumn, endColumn);

    private bool YieldRow(int rowIndex, int startColumn, int endColumn)
    {
        if (rowIndex <= 0 || startColumn > endColumn)
        {
            return false;
        }

        int width = endColumn - startColumn + 1;
        if (startColumn > 1)
        {
            _cellPool.AsSpan(startColumn - 1, width).CopyTo(_cellPool);
        }

        _currentWidth = width;
        _current = new ExcelRow(rowIndex, _cellPool.AsMemory(0, width), startColumn);
        return true;
    }

    private void ClearPreviousRow()
    {
        if (_currentWidth > 0)
        {
            _cellPool.AsSpan(0, _currentWidth).Clear();
            _currentWidth = 0;
        }
    }
}
