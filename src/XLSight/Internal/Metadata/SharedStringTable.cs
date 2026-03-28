using System.Net;
using System.Text;

namespace XLSight.Internal.Metadata;

/// <summary>
/// Lazy UTF-8 arena for shared strings. Stores all SST text as contiguous UTF-8 bytes
/// and materialises .NET <see cref="string"/> objects only on first access per index.
/// </summary>
internal sealed class SharedStringTable
{
    internal static readonly SharedStringTable Empty = new([], []);

    private readonly byte[] _arena;
    // Packed per entry: high 32 bits = start offset in arena, low 32 bits = byte length.
    private readonly long[] _info;
    private readonly string?[] _cache;

    internal int Count => _info.Length;

    internal SharedStringTable(byte[] arena, long[] info)
    {
        _arena = arena;
        _info = info;
        _cache = new string?[info.Length];
    }

    internal string GetString(int index)
    {
        if ((uint)index >= (uint)_info.Length) { return string.Empty; }
        if (_cache[index] is { } cached) { return cached; }

        long packed = _info[index];
        int start = (int)(packed >> 32);
        int length = (int)packed;

        if (length == 0) { return _cache[index] = string.Empty; }

        string value = Encoding.UTF8.GetString(_arena.AsSpan(start, length));
        // Fast SIMD scan before paying the allocation cost of unescaping.
        if (_arena.AsSpan(start, length).IndexOf((byte)'&') >= 0)
        {
            value = WebUtility.HtmlDecode(value);
        }
        return _cache[index] = value;
    }

    /// <summary>
    /// Returns the UTF-16 character count for a shared string entry without
    /// materialising a <see cref="string"/>. Zero allocation for the common case.
    /// Falls back to <see cref="GetString"/> when the arena bytes contain XML
    /// entities (e.g. <c>&amp;amp;</c>) so the decoded length is exact.
    /// </summary>
    internal int GetCharCount(int index)
    {
        if ((uint)index >= (uint)_info.Length) { return 0; }

        long packed = _info[index];
        int start = (int)(packed >> 32);
        int length = (int)packed;

        if (length == 0) { return 0; }

        var span = _arena.AsSpan(start, length);

        // Entity decoding changes character count — fall back to materialised string.
        if (span.IndexOf((byte)'&') >= 0)
        {
            return GetString(index).Length;
        }

        return Encoding.UTF8.GetCharCount(span);
    }
}
