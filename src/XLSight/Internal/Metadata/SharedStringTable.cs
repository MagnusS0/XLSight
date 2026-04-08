using System.Text;

namespace XLSight.Internal.Metadata;

/// <summary>
/// Lazy UTF-8 arena for shared strings. Stored as 64 KB chunks (below the 85 KB LOH
/// threshold). Strings materialise on demand via a fixed-size direct-mapped LRU cache.
/// Entities are pre-resolved at parse time so the chunks hold clean UTF-8.
/// </summary>
internal sealed class SharedStringTable
{
    // 64 KB arena chunks — below the 85 KB LOH threshold.
    private const int ArenaChunkBits = 16;
    private const int ArenaChunkSize = 1 << ArenaChunkBits; // 65 536
    private const int ArenaChunkMask = ArenaChunkSize - 1;

    // 8 192 longs × 8 bytes = 64 KB info chunks.
    private const int InfoChunkBits = 13;
    private const int InfoChunkSize = 1 << InfoChunkBits;   // 8 192
    private const int InfoChunkMask = InfoChunkSize - 1;

    // 8 192 slots × ~24 bytes ≈ 196 KB — fits in L2 cache.
    private const int CacheSlots = 8192;
    private const int CacheMask  = CacheSlots - 1;

    internal static readonly SharedStringTable Empty = new([], [], 0);

    private readonly byte[][] _arena;
    // high 32 bits: globalOffset = (arenaChunkIdx << ArenaChunkBits) | arenaChunkOffset
    // low  32 bits: byte length
    private readonly long[][] _info;
    private readonly int      _count;
    // Strings with few unique values stay hot; one-off strings are evicted and collected by Gen 0.
    private readonly (int Index, string Value)[] _cache;

    internal int Count => _count;

    internal SharedStringTable(byte[][] arena, long[][] info, int count)
    {
        _arena = arena;
        _info  = info;
        _count = count;
        _cache = new (int, string)[CacheSlots];
        Array.Fill(_cache, (-1, string.Empty));
    }

    internal string GetString(int index)
    {
        if ((uint)index >= (uint)_count) { return string.Empty; }

        int slot = index & CacheMask;
        ref var entry = ref _cache[slot];
        if (entry.Index == index) { return entry.Value; }

        string value = DecodeFromArena(index);
        entry = (index, value);
        return value;
    }

    /// <summary>
    /// Returns the UTF-16 character count without materialising a <see cref="string"/>.
    /// Always zero-allocation — the arena contains clean UTF-8.
    /// </summary>
    internal int GetCharCount(int index)
    {
        if ((uint)index >= (uint)_count) { return 0; }
        var span = GetUtf8Span(index);
        return span.IsEmpty ? 0 : Encoding.UTF8.GetCharCount(span);
    }

    /// <summary>
    /// Returns a zero-allocation span into the arena for the given SST entry.
    /// The span is valid for the lifetime of this <see cref="SharedStringTable"/>.
    /// </summary>
    internal ReadOnlySpan<byte> GetUtf8Bytes(int index)
    {
        if ((uint)index >= (uint)_count) { return default; }
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
}
