using XLSight.Internal.Sinks;

namespace XLSight.Internal.Readers.Xlsb;

internal sealed class XlsbSharedStringTable : ISharedStringSource, IDisposable
{
    // 512 references x 8 bytes = 4 KB chunks on x64, keeping early-exit reads small.
    private const int ChunkBits = 9;
    private const int ChunkSize = 1 << ChunkBits;
    private const int ChunkMask = ChunkSize - 1;

    internal static XlsbSharedStringTable Empty { get; } = new();

    internal static XlsbSharedStringTable Parse(Stream? stream) =>
        stream is null ? Empty : new XlsbSharedStringTable(stream);

    // Lock-free state:
    // _chunks is volatile to ensure outer-array resizing updates are visible instantly.
    // _parsedCount is volatile to ensure writes to the chunk slot are visible before the count increments.
    private volatile string?[][] _chunks;
    private volatile int _parsedCount;

    private XlsbRecordIterator? _iterator;
    private Stream? _stream;
    private readonly Lock _parseLock = new();
    private bool _disposed;

    private XlsbSharedStringTable()
    {
        _chunks = [];
        _parsedCount = 0;
    }

    internal XlsbSharedStringTable(string[] strings)
    {
        _chunks = CreateChunks(strings);
        _parsedCount = strings.Length;
    }

    internal XlsbSharedStringTable(Stream stream)
    {
        _stream = stream;
        _iterator = new XlsbRecordIterator(stream);

        // Read BrtBeginSst upfront to pre-size the outer chunk table using cstUnique
        // without allocating storage for strings First10 will never touch.
        int capacity = 0;
        while (_iterator.TryRead(out XlsbRecord record))
        {
            if (record.Type == XlsbRecordType.BrtBeginSst && record.Payload.Length >= 8)
            {
                capacity = (int)XlsbBinary.ReadUInt32(record.Payload, 4);
                break;
            }
        }

        _chunks = capacity > 0 ? new string?[GetChunkCount(capacity)][] : [];
        _parsedCount = 0;
    }

    internal int Count => ParseAll();

    internal int AllocatedChunkCount
    {
        get
        {
            int count = 0;
            foreach (string?[]? chunk in _chunks)
            {
                if (chunk is not null)
                {
                    count++;
                }
            }

            return count;
        }
    }

    public string GetString(int index)
    {
        // Copy volatile references to local variables to freeze the state for this check.
        string?[][] chunks = _chunks;
        int parsedCount = _parsedCount;

        // Lock-free path: Returns immediately if the string is already parsed and published.
        // Double-checking against chunks.Length guards against the resizing race condition.
        if ((uint)index < (uint)parsedCount)
        {
            int chunkIndex = index >> ChunkBits;
            if ((uint)chunkIndex < (uint)chunks.Length && chunks[chunkIndex] is { } chunk)
            {
                return chunk[index & ChunkMask] ?? string.Empty;
            }
        }

        return GetStringSlow(index);
    }

    public int GetCharCount(int index) => GetString(index).Length;

    private int ParseAll()
    {
        // Drive the stream to completion.
        GetStringSlow(int.MaxValue);
        return _parsedCount;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        lock (_parseLock)
        {
            FinalizeParsing();
        }
    }

    private string GetStringSlow(int index)
    {
        lock (_parseLock)
        {
            // Re-check under lock: another thread might have parsed this index while we waited.
            string?[][] chunks = _chunks;
            int parsedCount = _parsedCount;

            if ((uint)index < (uint)parsedCount)
            {
                return GetParsedString(chunks, index);
            }

            if (_iterator is null)
            {
                return string.Empty;
            }

            // Stream and populate chunks incrementally.
            while (_parsedCount <= index)
            {
                if (!_iterator.TryRead(out XlsbRecord record))
                {
                    FinalizeParsing();
                    break;
                }

                if (record.Type == XlsbRecordType.BrtSSTItem)
                {
                    string value = XlsbBinary.ReadRichStringText(record.Payload);
                    string?[] chunk = GetOrCreateChunk(_parsedCount);

                    chunk[_parsedCount & ChunkMask] = value;

                    // Volatile write (Release Barrier): publishes the written string to concurrent readers.
                    _parsedCount++;
                }
                else if (record.Type == XlsbRecordType.BrtEndSst)
                {
                    FinalizeParsing();
                    break;
                }
            }

            chunks = _chunks;
            parsedCount = _parsedCount;

            return (uint)index < (uint)parsedCount ? GetParsedString(chunks, index) : string.Empty;
        }
    }

    private string?[] GetOrCreateChunk(int index)
    {
        int chunkIndex = index >> ChunkBits;
        string?[][] chunks = _chunks;
        if ((uint)chunkIndex >= (uint)chunks.Length)
        {
            chunks = ResizeChunkArray(chunkIndex + 1);
        }

        string?[]? chunk = chunks[chunkIndex];
        if (chunk is null)
        {
            chunk = new string?[ChunkSize];
            chunks[chunkIndex] = chunk;
        }

        return chunk;
    }

    private string?[][] ResizeChunkArray(int requiredLength)
    {
        int currentLength = _chunks.Length;
        int newLength = currentLength == 0 ? 1 : currentLength;
        while (newLength < requiredLength)
        {
            newLength *= 2;
        }

        string?[][] newChunks = new string?[newLength][];
        Array.Copy(_chunks, newChunks, currentLength);

        // Volatile write ensures that the new outer array reference is published safely to readers.
        _chunks = newChunks;
        return newChunks;
    }

    private void FinalizeParsing()
    {
        if (_iterator is null)
        {
            return;
        }

        DisposeParserResources();
    }

    private void DisposeParserResources()
    {
        _iterator?.Dispose();
        _iterator = null;
        _stream?.Dispose();
        _stream = null;
    }

    private static int GetChunkCount(int capacity) => ((capacity - 1) >> ChunkBits) + 1;

    private static string GetParsedString(string?[][] chunks, int index)
    {
        int chunkIndex = index >> ChunkBits;
        if ((uint)chunkIndex >= (uint)chunks.Length || chunks[chunkIndex] is not { } chunk)
        {
            return string.Empty;
        }

        return chunk[index & ChunkMask] ?? string.Empty;
    }

    private static string?[][] CreateChunks(string[] strings)
    {
        if (strings.Length == 0)
        {
            return [];
        }

        string?[][] chunks = new string?[GetChunkCount(strings.Length)][];
        for (int offset = 0; offset < strings.Length; offset += ChunkSize)
        {
            int chunkIndex = offset >> ChunkBits;
            int length = Math.Min(ChunkSize, strings.Length - offset);
            string?[] chunk = new string?[ChunkSize];
            Array.Copy(strings, offset, chunk, 0, length);
            chunks[chunkIndex] = chunk;
        }

        return chunks;
    }
}
