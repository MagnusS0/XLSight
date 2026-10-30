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

    internal static bool IsTagNameBoundary(byte ch) => s_tagBoundaries.Contains(ch);

    internal static ReadOnlySpan<byte> ExtractUntilClose(ScanBuffer buf, ReadOnlySpan<byte> tagName)
    {
        int searchFrom = 0;
        while (true)
        {
            var span = buf.Span;
            var status = TryFindEndTag(span[searchFrom..], tagName, out int closeIdx, out int closeLen, out _);
            if (status == TagSearchResult.Found)
            {
                int absClose = searchFrom + closeIdx;
                buf.Advance(absClose + closeLen);
                return span[..absClose];
            }

            // NeedMoreData: partial '<' at searchFrom+closeIdx — resume from there.
            // NotFound: no '<' at all — resume from end (only scan fresh bytes after refill).
            searchFrom = status == TagSearchResult.NeedMoreData
                ? searchFrom + closeIdx
                : span.Length;

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

    /// <summary>
    /// Skips past the next end tag whose local name is <paramref name="localName"/>, searching
    /// for the name itself with a vectorized substring search instead of visiting every tag.
    /// </summary>
    /// <remarks>
    /// Used to discard whole elements (rows or cells outside a bounded range) whose content is
    /// never inspected. A candidate is accepted only when it is preceded by <c>&lt;/</c>
    /// (optionally <c>&lt;/prefix:</c>) and followed by optional whitespace and <c>&gt;</c>,
    /// which cannot occur in well-formed text or attribute values because <c>&lt;</c> must be
    /// escaped there. When the window holds no complete match, bytes from the last <c>&lt;</c>
    /// are kept so an end tag split across a refill boundary is still found.
    /// </remarks>
    internal static void SkipPastEndTag(ScanBuffer buf, ReadOnlySpan<byte> localName)
    {
        int searchFrom = 0;
        while (true)
        {
            var span = buf.Span;
            bool needMore = false;
            while (searchFrom < span.Length)
            {
                int rel = span[searchFrom..].IndexOf(localName);
                if (rel < 0) { break; }

                int nameStart = searchFrom + rel;
                var result = MatchEndTagAroundName(span, nameStart, localName.Length, out int tagEnd);
                if (result == TagSearchResult.Found)
                {
                    buf.Advance(tagEnd);
                    return;
                }

                if (result == TagSearchResult.NeedMoreData)
                {
                    needMore = true;
                    break;
                }

                searchFrom = nameStart + 1;
            }

            // Any end tag not yet complete must start at the last '<' in the window:
            // '<' cannot occur inside a tag, so earlier tags were complete and already searched.
            int keep = span.LastIndexOf((byte)'<');
            if (keep < 0)
            {
                buf.Advance(span.Length);
                searchFrom = 0;
                if (!buf.Refill()) { return; }
                continue;
            }

            if (!buf.CanReadMore)
            {
                // No further bytes can complete a partial match; mirror SkipToEndTag at EOF.
                if (needMore) { return; }
                buf.Advance(span.Length);
                return;
            }

            buf.Advance(keep);
            searchFrom = 0;
            if (!buf.Refill()) { return; }
        }
    }

    // Validates that the local name at nameStart belongs to an end tag: "</" [prefix ":"] name ws* ">".
    private static TagSearchResult MatchEndTagAroundName(
        ReadOnlySpan<byte> span, int nameStart, int nameLength, out int tagEnd)
    {
        tagEnd = 0;
        int j = nameStart - 1;
        if (j < 1) { return TagSearchResult.NotFound; }

        if (span[j] == (byte)':')
        {
            j--;
            while (j >= 0 && span[j] is not ((byte)'/' or (byte)'<' or (byte)'>') && !s_xmlWhitespace.Contains(span[j]))
            {
                j--;
            }

            if (j < 1) { return TagSearchResult.NotFound; }
        }

        if (span[j] != (byte)'/' || span[j - 1] != (byte)'<')
        {
            return TagSearchResult.NotFound;
        }

        int k = nameStart + nameLength;
        int gt = FindCloseAngleBracket(span, k);
        if (gt == -1) { return TagSearchResult.NeedMoreData; }
        if (gt == -2) { return TagSearchResult.NotFound; }

        tagEnd = gt + 1;
        return TagSearchResult.Found;
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
            int lt = span[search..].IndexOf((byte)'<');
            if (lt < 0) { return TagSearchResult.NotFound; }
            lt += search;

            var result = TryMatchStartTagAt(span[lt..], localName, out int afterName, out int endExcl, out bool selfClose);
            if (result == TagSearchResult.Found)
            {
                match = new StartTagMatch(lt, lt + afterName, lt + endExcl, selfClose);
                return TagSearchResult.Found;
            }
            if (result == TagSearchResult.NeedMoreData)
            {
                partialIndex = lt;
                match = new StartTagMatch(lt, 0, 0, false);
                return TagSearchResult.NeedMoreData;
            }
            search = lt + 1;
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
            int lt = span[search..].IndexOf((byte)'<');
            if (lt < 0) { return TagSearchResult.NotFound; }
            lt += search;

            var result = TryMatchEndTagAt(span[lt..], localName, out int len);
            if (result == TagSearchResult.Found)
            {
                tagStart = lt;
                tagLength = len;
                return TagSearchResult.Found;
            }
            if (result == TagSearchResult.NeedMoreData)
            {
                tagStart = lt;
                partialIndex = lt;
                return TagSearchResult.NeedMoreData;
            }
            search = lt + 1;
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
