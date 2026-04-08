using System.Text;

namespace XLSight.Internal.Metadata;

/// <summary>
/// Lazy UTF-8 arena for shared strings. Stores all SST text as contiguous UTF-8 bytes
/// and materialises .NET <see cref="string"/> objects only on demand via a fixed-size
/// direct-mapped cache.
/// </summary>
internal sealed class SharedStringTable
{
    // Power-of-2 so the slot lookup is a single bitwise AND.
    // 8 192 slots × ~24 bytes per entry ≈ 196 KB — fits comfortably in L2 cache.
    private const int CacheSlots = 8192;
    private const int CacheMask  = CacheSlots - 1;

    internal static readonly SharedStringTable Empty = new([], []);

    private readonly byte[] _arena;
    // Packed per entry: high 32 bits = start offset in arena, low 32 bits = byte length.
    private readonly long[] _info;
    // Fixed-size direct-mapped string cache keyed by SST index.
    // Categorical strings (few unique values, many references) stay hot in their slot.
    // Unique strings (addresses, descriptions) get evicted and collected by Gen 0 GC.
    // Single-writer-per-slot semantics: safe for the typical single-threaded streaming
    // workload. Concurrent writers to the same slot may produce a cache miss on the
    // next read (the slot check falls through to a fresh decode), never a wrong value,
    // because mismatched Index always causes a fall-through.
    private readonly (int Index, string Value)[] _cache;

    internal int Count => _info.Length;

    internal SharedStringTable(byte[] arena, long[] info)
    {
        _arena = arena;
        _info  = info;
        _cache = new (int, string)[CacheSlots];
        // Fill with -1 so that SST index 0 does not falsely hit on first access.
        Array.Fill(_cache, (-1, string.Empty));
    }

    internal string GetString(int index)
    {
        if ((uint)index >= (uint)_info.Length) { return string.Empty; }

        int slot = index & CacheMask;
        ref var entry = ref _cache[slot];
        if (entry.Index == index) { return entry.Value; }

        string value = DecodeFromArena(index);
        entry = (index, value);
        return value;
    }

    /// <summary>
    /// Returns the UTF-16 character count for a shared string entry without
    /// materialising a <see cref="string"/>. Always zero-allocation because the
    /// arena contains clean UTF-8 with entities already resolved at parse time.
    /// </summary>
    internal int GetCharCount(int index)
    {
        if ((uint)index >= (uint)_info.Length) { return 0; }

        long packed = _info[index];
        int start  = (int)(packed >> 32);
        int length = (int)packed;

        if (length == 0) { return 0; }

        return Encoding.UTF8.GetCharCount(_arena.AsSpan(start, length));
    }

    /// <summary>
    /// Returns the raw UTF-8 bytes for a shared string entry directly from the arena.
    /// Zero allocation — the span is a view into the arena, valid for the lifetime of
    /// this <see cref="SharedStringTable"/>.
    /// </summary>
    internal ReadOnlySpan<byte> GetUtf8Bytes(int index)
    {
        if ((uint)index >= (uint)_info.Length) { return default; }

        long packed = _info[index];
        int start  = (int)(packed >> 32);
        int length = (int)packed;

        return _arena.AsSpan(start, length);
    }

    private string DecodeFromArena(int index)
    {
        long packed = _info[index];
        int start  = (int)(packed >> 32);
        int length = (int)packed;

        if (length == 0) { return string.Empty; }

        // Arena contains clean UTF-8 — entities were resolved during SST parsing.
        return Encoding.UTF8.GetString(_arena.AsSpan(start, length));
    }
}
