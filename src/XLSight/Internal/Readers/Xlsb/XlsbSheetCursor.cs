using System.Buffers;
using XLSight.Internal.Metadata;

namespace XLSight.Internal.Readers.Xlsb;

internal sealed class XlsbSheetCursor : IRowCursor
{
    private readonly XlsbRecordIterator _iterator;
    private readonly XlsbRowScanner _scanner;
    private readonly ExcelCellValue[] _cellPool;
    private bool _disposed;

    internal XlsbSheetCursor(
        Stream worksheetStream,
        Lazy<XlsbSharedStringTable> sharedStrings,
        StyleTable styles,
        bool isDate1904,
        ReadMode mode,
        ExcelRange range,
        XlsbFormulaContext? formulaContext = null)
    {
        _cellPool = ArrayPool<ExcelCellValue>.Shared.Rent(ExcelLimits.MaxColumns);
        _iterator = new XlsbRecordIterator(worksheetStream);
        _scanner = new XlsbRowScanner(
            _iterator,
            sharedStrings,
            styles,
            isDate1904,
            mode,
            range,
            _cellPool,
            formulaContext);
    }

    public ExcelRow Current => _scanner.Current;

    public bool IsSheetDone => _scanner.IsDone;

    public bool MoveNext()
    {
        if (_disposed)
        {
            return false;
        }

        return _scanner.MoveNext();
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

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        try
        {
            _scanner.Dispose();
        }
        finally
        {
            _iterator.Dispose();
            ArrayPool<ExcelCellValue>.Shared.Return(_cellPool, clearArray: false);
        }
    }
}
