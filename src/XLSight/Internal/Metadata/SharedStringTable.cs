using System.Buffers;
using System.Text;
using XLSight.Internal.Readers.Xlsx;
using XLSight.Internal.Sinks;

namespace XLSight.Internal.Metadata;

/// <summary>
/// Lazy UTF-8 arena for shared strings. Stored as 64 KB chunks (below the 85 KB LOH
/// threshold). Strings materialise on demand via a capped index cache — low-index entries
/// are exact-match cached; high-index entries bypass and are collected by Gen 0.
/// Entities are pre-resolved at parse time so the chunks hold clean UTF-8.
/// The SST is parsed incrementally: only enough <c>&lt;si&gt;</c> entries are decoded to
/// satisfy each <see cref="GetString"/> call, so early-exit sheet scans avoid loading the
/// full table. Once the stream is exhausted the lazy state is released and subsequent
/// lookups hit the arena directly with no locking overhead.
/// </summary>
internal sealed class SharedStringTable : ISharedStringSource, IDisposable
{
    // 64 KB arena chunks — below the 85 KB LOH threshold.
    private const int ArenaChunkBits = 16;
    private const int ArenaChunkSize = 1 << ArenaChunkBits; // 65 536
    private const int ArenaChunkMask = ArenaChunkSize - 1;

    // 8 192 longs × 8 bytes = 64 KB info chunks.
    private const int InfoChunkBits = 13;
    private const int InfoChunkSize = 1 << InfoChunkBits;   // 8 192
    private const int InfoChunkMask = InfoChunkSize - 1;

    // 131 072 entries × 8 bytes = 1 MB — covers low-index clustered strings.
    private const int MaxCacheEntries = 131072;

    // Cache slots are allocated in 4 096-entry (32 KB) chunks on first touch, so an
    // early-exit read of a large SST does not pay for the full 1 MB slot array up front.
    private const int CacheChunkBits = 12;
    private const int CacheChunkSize = 1 << CacheChunkBits;
    private const int CacheChunkMask = CacheChunkSize - 1;

    internal static readonly SharedStringTable Empty = new();

    private readonly List<byte[]> _arena;
    private readonly List<long[]> _info;
    // Low-index strings are clustered and highly repeated; high-index strings bypass and die in Gen 0.
    private readonly string?[]?[] _cache;

    // Lazy-parsing state — all null once _isComplete is true.
    private SharedStringsByteParser.ParseState? _parseState;
    private Stream? _sstStream;
    private ScanBuffer? _sstBuffer;
    private byte[]? _stagingBuf;
    private readonly Lock _pumpLock = new();
    private readonly int _cacheLength;
    private readonly int _declaredUniqueCount = -1;

    // Publication protocol: the pump writes an entry's arena bytes and info slot (and any List
    // growth) and then publishes it with a release write of _parsedCount; completion is published
    // with a release write of _isComplete after the final count. Lock-free readers acquire-read
    // _parsedCount, and re-read it after observing _isComplete, so on weakly ordered CPUs
    // (ARM64) they never see a count ahead of the data it covers or miss the final count.
    private int  _parsedCount;
    private bool _isComplete;

    /// <summary>
    /// Number of strings parsed so far. Accessing this property pumps the parser to
    /// completion so the full count is returned — avoid in hot paths.
    /// </summary>
    internal int Count
    {
        get => EnsureParsed(int.MaxValue);
    }

    /// <summary>Empty singleton — no <c>xl/sharedStrings.xml</c> entry in the workbook.</summary>
    private SharedStringTable()
    {
        _arena      = [];
        _info       = [];
        _cache      = [];
        _cacheLength = 0;
        _isComplete = true;
    }

    /// <summary>
    /// Lazy SST — parsing is deferred to the first <see cref="GetString"/> call that
    /// requests an index not yet in the arena.
    /// </summary>
    internal SharedStringTable(
        Stream stream,
        ScanBuffer buffer,
        SharedStringsByteParser.ParseState state,
        byte[] stagingBuf,
        int declaredUniqueCount)
    {
        _sstStream  = stream;
        _sstBuffer  = buffer;
        _parseState = state;
        _stagingBuf = stagingBuf;
        _arena      = state.ArenaChunks;
        _info       = state.InfoChunks;
        _cacheLength = GetCacheLength(declaredUniqueCount);
        _cache      = new string?[]?[(_cacheLength + CacheChunkMask) >> CacheChunkBits];
        _declaredUniqueCount = declaredUniqueCount;
        _isComplete = false;
    }

    internal int CacheLength => _cacheLength;
    internal int FirstInfoChunkLength => _info.Count == 0 ? 0 : _info[0].Length;

    public string GetString(int index)
    {
        if ((uint)index >= (uint)Volatile.Read(ref _parsedCount) && (uint)index >= (uint)EnsureParsed(index))
        {
            return string.Empty;
        }

        if (index < _cacheLength)
        {
            string?[] chunk = _cache[index >> CacheChunkBits] ?? CreateCacheChunk(index >> CacheChunkBits);
            return chunk[index & CacheChunkMask] ??= DecodeFromArena(index);
        }

        return DecodeFromArena(index);
    }

    private string?[] CreateCacheChunk(int chunkIndex)
    {
        int length = Math.Min(CacheChunkSize, _cacheLength - (chunkIndex << CacheChunkBits));
        var created = new string?[length];
        return Interlocked.CompareExchange(ref _cache[chunkIndex], created, null) ?? created;
    }

    /// <summary>
    /// Returns the UTF-16 character count without materialising a <see cref="string"/>.
    /// Always zero-allocation — the arena contains clean UTF-8.
    /// </summary>
    public int GetCharCount(int index)
    {
        if ((uint)index >= (uint)Volatile.Read(ref _parsedCount) && (uint)index >= (uint)EnsureParsed(index))
        {
            return 0;
        }

        var span = GetUtf8Span(index);
        return span.IsEmpty ? 0 : Encoding.UTF8.GetCharCount(span);
    }

    /// <summary>
    /// Returns a zero-allocation span into the arena for the given SST entry.
    /// The span is valid for the lifetime of this <see cref="SharedStringTable"/>.
    /// </summary>
    internal ReadOnlySpan<byte> GetUtf8Bytes(int index)
    {
        if ((uint)index >= (uint)Volatile.Read(ref _parsedCount) && (uint)index >= (uint)EnsureParsed(index))
        {
            return default;
        }

        return GetUtf8Span(index);
    }

    private ReadOnlySpan<byte> GetUtf8Span(int index)
    {
        long packed      = _info[index >> InfoChunkBits][index & InfoChunkMask];
        int globalOffset = (int)(packed >> 32);
        int length       = (int)packed;

        if (length == 0) { return default; }

        int chunkIdx    = globalOffset >> ArenaChunkBits;
        int chunkOffset = globalOffset & ArenaChunkMask;

        return _arena[chunkIdx].AsSpan(chunkOffset, length);
    }

    private string DecodeFromArena(int index)
    {
        var span = GetUtf8Span(index);
        return span.IsEmpty ? string.Empty : Encoding.UTF8.GetString(span);
    }

    /// <summary>
    /// Pumps the SST stream until <paramref name="targetIndex"/> has been parsed or the
    /// stream is exhausted, and returns the published entry count. Indexes below the
    /// returned count are safe to read without the lock.
    /// </summary>
    private int EnsureParsed(int targetIndex)
    {
        if (!Volatile.Read(ref _isComplete))
        {
            lock (_pumpLock)
            {
                // Once the declared uniqueCount is reached, probe once more: on a well-formed SST
                // this hits </sst>, completes the table and releases the stream, the 64 KB scan
                // window and the 256 KB staging buffer instead of holding them until Dispose.
                int parsed = _parsedCount;
                while (!_isComplete && (parsed <= targetIndex || parsed == _declaredUniqueCount))
                {
                    if (!SharedStringsByteParser.FindNextSiCandidate(_sstBuffer!))
                    {
                        Volatile.Write(ref _isComplete, true);
                        CleanupParserResources();
                        break;
                    }
                    SharedStringsByteParser.DispatchSiElement(_sstBuffer!, _parseState!);
                    Volatile.Write(ref _parsedCount, ++parsed);
                }
            }
        }

        // Acquire after observing completion (or after the lock): sees the final count.
        return Volatile.Read(ref _parsedCount);
    }

    private void CleanupParserResources()
    {
        _sstBuffer?.Dispose();
        _sstBuffer = null;
        // ScanBuffer does not own its source; release the zip entry (inflater) stream here.
        _sstStream?.Dispose();
        _sstStream = null;
        if (_stagingBuf is not null)
        {
            ArrayPool<byte>.Shared.Return(_stagingBuf, clearArray: false);
            _stagingBuf = null;
        }
        _parseState = null;
    }

    public void Dispose()
    {
        if (Volatile.Read(ref _isComplete)) { return; }
        lock (_pumpLock)
        {
            if (_isComplete) { return; }
            Volatile.Write(ref _isComplete, true);
            CleanupParserResources();
        }
    }

    private static int GetCacheLength(int declaredUniqueCount)
    {
        if (declaredUniqueCount < 0)
        {
            return MaxCacheEntries;
        }

        return Math.Min(declaredUniqueCount, MaxCacheEntries);
    }
}
