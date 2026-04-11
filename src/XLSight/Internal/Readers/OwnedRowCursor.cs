namespace XLSight.Internal.Readers;

internal sealed class OwnedRowCursor : IRowCursor
{
    private readonly Stream _stream;
    private readonly IRowCursor _cursor;
    private bool _disposed;

    internal OwnedRowCursor(Stream stream, IRowCursor cursor)
    {
        _stream = stream;
        _cursor = cursor;
    }

    public ExcelRow Current => _cursor.Current;

    public bool IsSheetDone => _cursor.IsSheetDone;

    public bool MoveNext() => _cursor.MoveNext();

    public bool TryParseNext(out ExcelRow row) => _cursor.TryParseNext(out row);

    public ValueTask<bool> RefillAsync(CancellationToken ct = default) => _cursor.RefillAsync(ct);

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        try
        {
            _cursor.Dispose();
        }
        finally
        {
            _stream.Dispose();
        }
    }
}
