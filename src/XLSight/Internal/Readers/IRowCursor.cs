namespace XLSight.Internal.Readers;

internal interface IRowCursor : IDisposable
{
    public ExcelRow Current { get; }
    public bool IsSheetDone { get; }

    public bool MoveNext();
    public bool TryParseNext(out ExcelRow row);
    public ValueTask<bool> RefillAsync(CancellationToken ct = default);
}
