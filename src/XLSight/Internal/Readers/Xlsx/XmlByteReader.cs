using System.Buffers;

namespace XLSight.Internal.Readers.Xlsx;

/// <summary>
/// Shared low-level helpers for forward-only XML scanning on UTF-8 byte streams.
/// Reuses the same SIMD-friendly tag and attribute search style as the sheet scanner.
/// </summary>
internal static class XmlByteReader
{
    internal static readonly SearchValues<byte> s_tagBoundaries = SearchValues.Create(">/ \t\r\n"u8);
    internal static readonly SearchValues<byte> s_xmlWhitespace = SearchValues.Create(" \t\r\n"u8);
    internal const int NeedMoreDataSentinel = int.MinValue;

    internal static bool IsTagNameBoundary(byte ch) => s_tagBoundaries.Contains(ch);

    internal static ReadOnlySpan<byte> ExtractUntilClose(ScanBuffer buf, ReadOnlySpan<byte> tagName)
    {
        while (true)
        {
            var span = buf.Span;
            var status = TryFindEndTag(span, tagName, out int closeIdx, out int closeLen, out _);
            if (status == TagSearchResult.Found)
            {
                var value = span[..closeIdx];
                buf.Advance(closeIdx + closeLen);
                return value;
            }

            if (!buf.CanReadMore || !buf.Refill())
            {
                return ReadOnlySpan<byte>.Empty;
            }
        }
    }

    internal static void SkipToEndTag(ScanBuffer buf, ReadOnlySpan<byte> tagName)
    {
        while (true)
        {
            var span = buf.Span;
            var status = TryFindEndTag(span, tagName, out int idx, out int len, out int partialIndex);
            if (status == TagSearchResult.Found)
            {
                buf.Advance(idx + len);
                return;
            }

            if (status == TagSearchResult.NeedMoreData)
            {
                if (idx >= 0)
                {
                    buf.Advance(idx);
                    if (!buf.Refill())
                    {
                        return;
                    }
                }
                else if (!RefillKeepingTagStart(buf, span, partialIndex))
                {
                    return;
                }

                continue;
            }

            if (!RefillKeepingTagStart(buf, span, partialIndex))
            {
                return;
            }
        }
    }

    internal static bool RefillKeepingTagStart(ScanBuffer buf, ReadOnlySpan<byte> span, int partialIndex)
    {
        int keepIndex = partialIndex;
        if (keepIndex < 0)
        {
            keepIndex = span.LastIndexOf((byte)'<');
        }

        if (keepIndex > 0)
        {
            buf.Advance(keepIndex);
            return buf.Refill();
        }

        if (keepIndex == 0 && buf.CanReadMore)
        {
            return buf.Refill();
        }

        if (partialIndex >= 0)
        {
            buf.Advance(Math.Min(1, span.Length));
            return buf.Refill();
        }

        int safe = span.Length;
        buf.Advance(safe);
        return buf.Refill();
    }

    internal static TagSearchResult TryFindStartTag(
        ReadOnlySpan<byte> span,
        ReadOnlySpan<byte> localName,
        out StartTagMatch match,
        out int partialIndex)
    {
        match = default;
        partialIndex = -1;
        int search = 0;

        while (true)
        {
            int nameIdx = span[search..].IndexOf(localName);
            if (nameIdx < 0) { return TagSearchResult.NotFound; }
            nameIdx += search;

            if (!TryGetOpenTagStart(span, nameIdx, out int tagStart))
            {
                if (tagStart == NeedMoreDataSentinel)
                {
                    partialIndex = Math.Max(0, nameIdx - 1);
                    match = new StartTagMatch(partialIndex, 0, 0, false);
                    return TagSearchResult.NeedMoreData;
                }
                search = nameIdx + 1;
                continue;
            }

            int nameEnd = nameIdx + localName.Length;
            if (nameEnd >= span.Length) { partialIndex = tagStart; match = new StartTagMatch(tagStart, 0, 0, false); return TagSearchResult.NeedMoreData; }
            if (!IsTagNameBoundary(span[nameEnd])) { search = nameIdx + 1; continue; }

            int gt = span[nameEnd..].IndexOf((byte)'>');
            if (gt < 0) { partialIndex = tagStart; match = new StartTagMatch(tagStart, 0, 0, false); return TagSearchResult.NeedMoreData; }

            int endExclusive = nameEnd + gt + 1;
            match = new StartTagMatch(tagStart, nameEnd, endExclusive, IsSelfClosing(span, nameEnd, endExclusive - 1));
            return TagSearchResult.Found;
        }
    }

    internal static TagSearchResult TryFindEndTag(
        ReadOnlySpan<byte> span,
        ReadOnlySpan<byte> localName,
        out int tagStart,
        out int tagLength,
        out int partialIndex)
    {
        tagStart = -1;
        tagLength = 0;
        partialIndex = -1;
        int search = 0;

        while (true)
        {
            int nameIdx = span[search..].IndexOf(localName);
            if (nameIdx < 0) { return TagSearchResult.NotFound; }
            nameIdx += search;

            if (!TryGetCloseTagStart(span, nameIdx, out int ts))
            {
                if (ts == NeedMoreDataSentinel) { partialIndex = Math.Max(0, nameIdx - 2); return TagSearchResult.NeedMoreData; }
                search = nameIdx + 1;
                continue;
            }

            int nameEnd = nameIdx + localName.Length;
            if (nameEnd >= span.Length) { partialIndex = ts; return TagSearchResult.NeedMoreData; }

            int closeGt = FindCloseAngleBracket(span, nameEnd);
            if (closeGt == -1) { partialIndex = ts; return TagSearchResult.NeedMoreData; }
            if (closeGt == -2) { search = nameIdx + 1; continue; }

            tagStart = ts;
            tagLength = closeGt - ts + 1;
            return TagSearchResult.Found;
        }
    }

    internal static int MaxPartial(int left, int right) => left >= right ? left : right;

    private static int FindCloseAngleBracket(ReadOnlySpan<byte> span, int cursor)
    {
        var slice = span[cursor..];
        int idx = slice.IndexOfAnyExcept(s_xmlWhitespace);
        if (idx < 0)
        {
            return -1;
        }

        return slice[idx] == (byte)'>' ? cursor + idx : -2;
    }

    private static bool TryGetOpenTagStart(ReadOnlySpan<byte> span, int nameIdx, out int tagStart)
    {
        tagStart = NeedMoreDataSentinel;
        if (nameIdx == 0)
        {
            return false;
        }

        byte before = span[nameIdx - 1];
        if (before == (byte)'<')
        {
            tagStart = nameIdx - 1;
            return true;
        }

        if (before == (byte)':')
        {
            int i = nameIdx - 2;
            while (i >= 0 && IsValidPrefixChar(span[i]))
            {
                i--;
            }

            if (i < 0)
            {
                return false;
            }

            if (span[i] == (byte)'<')
            {
                tagStart = i;
                return true;
            }
        }

        tagStart = -2;
        return false;
    }

    private static bool TryGetCloseTagStart(ReadOnlySpan<byte> span, int nameIdx, out int tagStart)
    {
        // nameIdx == 0: impossible to have "</prefix" before position 0 — definitive non-match.
        // nameIdx == 1: span[0] could be '/' with '<' just before the buffer start — NeedMoreData.
        if (nameIdx == 0)
        {
            tagStart = -2;
            return false;
        }

        tagStart = NeedMoreDataSentinel;
        if (nameIdx < 2)
        {
            return false;
        }

        byte before = span[nameIdx - 1];
        if (before == (byte)'/' && span[nameIdx - 2] == (byte)'<')
        {
            tagStart = nameIdx - 2;
            return true;
        }

        if (before == (byte)':')
        {
            int i = nameIdx - 2;
            while (i >= 0 && IsValidPrefixChar(span[i]))
            {
                i--;
            }

            if (i < 1)
            {
                return false;
            }

            if (span[i] == (byte)'/' && span[i - 1] == (byte)'<')
            {
                tagStart = i - 1;
                return true;
            }
        }

        tagStart = -2;
        return false;
    }

    private static bool IsSelfClosing(ReadOnlySpan<byte> span, int attrStart, int gtIndex)
    {
        for (int i = gtIndex - 1; i >= attrStart; i--)
        {
            byte ch = span[i];
            if (s_xmlWhitespace.Contains(ch))
            {
                continue;
            }

            return ch == (byte)'/';
        }

        return false;
    }

    private static bool IsValidPrefixChar(byte ch) =>
        ch is >= (byte)'a' and <= (byte)'z'
            or >= (byte)'A' and <= (byte)'Z'
            or >= (byte)'0' and <= (byte)'9'
            or (byte)'_'
            or (byte)'-'
            or (byte)'.';

}
