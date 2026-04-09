using System.Buffers;
using System.Text;
using XLSight.Internal.Readers.Xlsx;

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
internal sealed class SharedStringTable : IDisposable
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

    internal static readonly SharedStringTable Empty = new();

    private readonly List<byte[]> _arena;
    private readonly List<long[]> _info;
    // Low-index strings are clustered and highly repeated; high-index strings bypass and die in Gen 0.
    private readonly string?[] _cache;

    // Lazy-parsing state — all null once _isComplete is true.
    private SharedStringsByteParser.ParseState? _parseState;
    private ScanBuffer? _sstBuffer;
    private byte[]? _stagingBuf;
    private readonly Lock _pumpLock = new();

    private int  _parsedCount;
    private bool _isComplete;

    /// <summary>
    /// Number of strings parsed so far. Accessing this property pumps the parser to
    /// completion so the full count is returned — avoid in hot paths.
    /// </summary>
    internal int Count
    {
        get
        {
            EnsureParsed(int.MaxValue);
            return _parsedCount;
        }
    }

    /// <summary>Empty singleton — no <c>xl/sharedStrings.xml</c> entry in the workbook.</summary>
    private SharedStringTable()
    {
        _arena      = [];
        _info       = [];
        _cache      = [];
        _isComplete = true;
    }

    /// <summary>
    /// Lazy SST — parsing is deferred to the first <see cref="GetString"/> call that
    /// requests an index not yet in the arena.
    /// </summary>
    internal SharedStringTable(
        ScanBuffer buffer,
        SharedStringsByteParser.ParseState state,
        byte[] stagingBuf)
    {
        _sstBuffer  = buffer;
        _parseState = state;
        _stagingBuf = stagingBuf;
        _arena      = state.ArenaChunks;
        _info       = state.InfoChunks;
        _cache      = new string?[MaxCacheEntries];
        _isComplete = false;
    }

    internal string GetString(int index)
    {
        if (index >= _parsedCount && !_isComplete)
        {
            EnsureParsed(index);
        }

        if ((uint)index >= (uint)_parsedCount) { return string.Empty; }

        if (index < _cache.Length)
        {
            return _cache[index] ??= DecodeFromArena(index);
        }

        return DecodeFromArena(index);
    }

    /// <summary>
    /// Returns the UTF-16 character count without materialising a <see cref="string"/>.
    /// Always zero-allocation — the arena contains clean UTF-8.
    /// </summary>
    internal int GetCharCount(int index)
    {
        if (index >= _parsedCount && !_isComplete) { EnsureParsed(index); }
        if ((uint)index >= (uint)_parsedCount) { return 0; }
        var span = GetUtf8Span(index);
        return span.IsEmpty ? 0 : Encoding.UTF8.GetCharCount(span);
    }

    /// <summary>
    /// Returns a zero-allocation span into the arena for the given SST entry.
    /// The span is valid for the lifetime of this <see cref="SharedStringTable"/>.
    /// </summary>
    internal ReadOnlySpan<byte> GetUtf8Bytes(int index)
    {
        if (index >= _parsedCount && !_isComplete) { EnsureParsed(index); }
        if ((uint)index >= (uint)_parsedCount) { return default; }
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
    /// stream is exhausted. No-op once <see cref="_isComplete"/> is true.
    /// </summary>
    private void EnsureParsed(int targetIndex)
    {
        if (_isComplete) { return; }
        lock (_pumpLock)
        {
            while (!_isComplete && _parsedCount <= targetIndex)
            {
                if (!SharedStringsByteParser.FindNextSiCandidate(_sstBuffer!))
                {
                    _isComplete = true;
                    CleanupParserResources();
                    break;
                }
                SharedStringsByteParser.DispatchSiElement(_sstBuffer!, _parseState!);
                _parsedCount++;
            }
        }
    }

    private void CleanupParserResources()
    {
        _sstBuffer?.Dispose();
        _sstBuffer = null;
        if (_stagingBuf is not null)
        {
            ArrayPool<byte>.Shared.Return(_stagingBuf, clearArray: false);
            _stagingBuf = null;
        }
        _parseState = null;
    }

    public void Dispose()
    {
        if (_isComplete) { return; }
        lock (_pumpLock)
        {
            if (_isComplete) { return; }
            _isComplete = true;
            CleanupParserResources();
        }
    }
}
