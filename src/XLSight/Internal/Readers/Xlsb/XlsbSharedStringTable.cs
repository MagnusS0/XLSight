using XLSight.Internal.Sinks;

namespace XLSight.Internal.Readers.Xlsb;

internal sealed class XlsbSharedStringTable : ISharedStringSource, IDisposable
{
    internal static XlsbSharedStringTable Empty { get; } = new([]);

    private readonly List<string> _strings;
    private readonly XlsbRecordIterator? _iterator;
    private readonly Stream? _stream;
    private readonly Lock _pumpLock = new();
    private volatile bool _isComplete;
    private bool _disposed;

    internal XlsbSharedStringTable(string[] strings)
    {
        _strings = [.. strings];
        _isComplete = true;
    }

    internal XlsbSharedStringTable(Stream stream)
    {
        _strings = [];
        _stream = stream;
        _iterator = new XlsbRecordIterator(stream);
    }

    internal int Count
    {
        get
        {
            EnsureParsed(int.MaxValue);
            return _strings.Count;
        }
    }

    public string GetString(int index)
    {
        if (index >= _strings.Count && !_isComplete)
        {
            EnsureParsed(index);
        }

        return (uint)index < (uint)_strings.Count ? _strings[index] : string.Empty;
    }

    public int GetCharCount(int index) => GetString(index).Length;

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _isComplete = true;
        _iterator?.Dispose();
        _stream?.Dispose();
    }

    private void EnsureParsed(int targetIndex)
    {
        if (_isComplete)
        {
            return;
        }

        lock (_pumpLock)
        {
            while (!_isComplete && _strings.Count <= targetIndex)
            {
                if (!_iterator!.TryRead(out XlsbRecord record))
                {
                    _isComplete = true;
                    break;
                }

                if (record.Type == XlsbRecordType.BrtSSTItem)
                {
                    _strings.Add(XlsbBinary.ReadRichStringText(record.Payload));
                }
            }
        }
    }
}
