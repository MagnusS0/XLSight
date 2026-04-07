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
            int lt = span.IndexOf((byte)'<');
            if (lt < 0)
            {
                buf.Advance(span.Length);
                if (!buf.Refill()) { return; }
                continue;
            }

            if (lt > 0) { buf.Advance(lt); }
            span = buf.Span;

            var result = TryMatchEndTagAt(span, tagName, out int tagLength);
            if (result == TagSearchResult.Found)
            {
                buf.Advance(tagLength);
                return;
            }
            if (result == TagSearchResult.NeedMoreData)
            {
                if (!buf.CanReadMore || !buf.Refill()) { return; }
                continue;
            }

            // Not our end tag — skip past this tag's '>'.
            SkipPastClosingBracket(buf);
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

    internal static bool IsValidPrefixChar(byte ch) =>
        ch is >= (byte)'a' and <= (byte)'z'
            or >= (byte)'A' and <= (byte)'Z'
            or >= (byte)'0' and <= (byte)'9'
            or (byte)'_'
            or (byte)'-'
            or (byte)'.';

    // ── <-first tag matching ─────────────────────────────────────────────────
    // These helpers inspect a span starting at '<' and determine whether it
    // matches a given local name (with optional namespace prefix).  Used by
    // the hot-path row/cell scanner to avoid full-buffer substring searches.

    /// <summary>
    /// Given a span whose first byte is <c>'&lt;'</c>, checks whether this is
    /// a start tag whose local name (ignoring any namespace prefix) equals
    /// <paramref name="localName"/>.
    /// All positions in the out parameters are relative to <paramref name="spanFromLt"/>[0].
    /// </summary>
    internal static TagSearchResult TryMatchStartTagAt(
        ReadOnlySpan<byte> spanFromLt,
        ReadOnlySpan<byte> localName,
        out int afterName,
        out int endExclusive,
        out bool isSelfClosing)
    {
        afterName = 0;
        endExclusive = 0;
        isSelfClosing = false;

        int len = spanFromLt.Length;
        if (len < 2)
        {
            return TagSearchResult.NeedMoreData;
        }

        // Not a start tag — end tag, PI, or comment/CDATA.
        byte first = spanFromLt[1];
        if (first is (byte)'/' or (byte)'?' or (byte)'!')
        {
            return TagSearchResult.NotFound;
        }

        // Walk past the full tag name (optional prefix + ':' + local name).
        int cursor = 1;
        int colonPos = -1;

        while (cursor < len)
        {
            byte ch = spanFromLt[cursor];
            if (ch == (byte)':') { colonPos = cursor; cursor++; continue; }
            if (IsTagNameBoundary(ch)) { break; }
            cursor++;
        }

        if (cursor >= len)
        {
            return TagSearchResult.NeedMoreData;
        }

        // Extract and compare the local-name portion.
        int localStart = colonPos >= 0 ? colonPos + 1 : 1;
        int localLen = cursor - localStart;

        if (localLen != localName.Length ||
            !spanFromLt.Slice(localStart, localLen).SequenceEqual(localName))
        {
            return TagSearchResult.NotFound;
        }

        // Tag name matches — find closing '>'.
        afterName = cursor;
        int gt = spanFromLt[cursor..].IndexOf((byte)'>');
        if (gt < 0)
        {
            return TagSearchResult.NeedMoreData;
        }

        endExclusive = cursor + gt + 1;
        isSelfClosing = IsSelfClosing(spanFromLt, cursor, cursor + gt);
        return TagSearchResult.Found;
    }

    /// <summary>
    /// Given a span whose first byte is <c>'&lt;'</c>, checks whether this is
    /// an end tag whose local name (ignoring any namespace prefix) equals
    /// <paramref name="localName"/>.
    /// <paramref name="tagLength"/> is the total length from <c>'&lt;'</c>
    /// through (and including) <c>'&gt;'</c>.
    /// </summary>
    internal static TagSearchResult TryMatchEndTagAt(
        ReadOnlySpan<byte> spanFromLt,
        ReadOnlySpan<byte> localName,
        out int tagLength)
    {
        tagLength = 0;
        int len = spanFromLt.Length;

        if (len < 3)
        {
            return TagSearchResult.NeedMoreData;
        }

        if (spanFromLt[1] != (byte)'/')
        {
            return TagSearchResult.NotFound;
        }

        // Walk past the full tag name after '</'.
        int cursor = 2;
        int colonPos = -1;

        while (cursor < len)
        {
            byte ch = spanFromLt[cursor];
            if (ch == (byte)':') { colonPos = cursor; cursor++; continue; }
            if (ch == (byte)'>' || s_xmlWhitespace.Contains(ch)) { break; }
            cursor++;
        }

        if (cursor >= len)
        {
            return TagSearchResult.NeedMoreData;
        }

        int localStart = colonPos >= 0 ? colonPos + 1 : 2;
        int localLen = cursor - localStart;

        if (localLen != localName.Length ||
            !spanFromLt.Slice(localStart, localLen).SequenceEqual(localName))
        {
            return TagSearchResult.NotFound;
        }

        // Expect '>' (possibly preceded by whitespace).
        int closeGt = FindCloseAngleBracket(spanFromLt, cursor);
        if (closeGt == -1)
        {
            return TagSearchResult.NeedMoreData;
        }

        if (closeGt == -2)
        {
            return TagSearchResult.NotFound;
        }

        tagLength = closeGt + 1;
        return TagSearchResult.Found;
    }

    /// <summary>
    /// Advances <paramref name="buf"/> past the next <c>'&gt;'</c>.
    /// Used to skip irrelevant tags in the &lt;-first scanner.
    /// </summary>
    internal static void SkipPastClosingBracket(ScanBuffer buf)
    {
        while (true)
        {
            var span = buf.Span;
            int gt = span.IndexOf((byte)'>');
            if (gt >= 0) { buf.Advance(gt + 1); return; }
            buf.Advance(span.Length);
            if (!buf.Refill()) { return; }
        }
    }

}
