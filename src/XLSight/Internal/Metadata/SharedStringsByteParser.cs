using System.Buffers;
using XLSight.Internal.Readers.Xlsx;

namespace XLSight.Internal.Metadata;

/// <summary>
/// Allocation-free SST parser that scans raw UTF-8 bytes instead of using XmlReader.
/// Stores raw bytes (including XML entities like &amp;amp;) into an arena; entity
/// decoding happens lazily in <see cref="SharedStringTable.GetString"/>.
/// </summary>
internal static class SharedStringsByteParser
{
    // Bytes of context kept at the tail of the scan window when no pattern is found,
    // so that tags straddling a buffer boundary are not missed.
    // Must be > max(prefix + ':' + '<') length, i.e. > ~11 bytes for any realistic prefix.
    private const int ContextKeep = 20;

    internal static SharedStringTable Parse(Stream stream)
    {
        byte[] arenaBuf = ArrayPool<byte>.Shared.Rent(16 * 1024 * 1024);
        long[] infoBuf = ArrayPool<long>.Shared.Rent(1024 * 1024);
        int arenaOffset = 0;
        int infoCount = 0;

        try
        {
            using var buf = new ScanBuffer(stream);
            ScanDocument(buf, ref arenaBuf, ref arenaOffset, ref infoBuf, ref infoCount);
            return new SharedStringTable(
                arenaBuf.AsSpan(0, arenaOffset).ToArray(),
                infoBuf.AsSpan(0, infoCount).ToArray());
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(arenaBuf, clearArray: false);
            ArrayPool<long>.Shared.Return(infoBuf, clearArray: false);
        }
    }

    // Phase 1: SIMD-scan for <si> elements across the whole document.
    private static void ScanDocument(
        ScanBuffer buf,
        ref byte[] arenaBuf,
        ref int arenaOffset,
        ref long[] infoBuf,
        ref int infoCount)
    {
        while (FindNextSiCandidate(buf))
        {
            DispatchSiElement(buf, ref arenaBuf, ref arenaOffset, ref infoBuf, ref infoCount);
        }
    }

    // Scans forward until the buffer is positioned just after the "si" of a valid <si…> tag.
    // Returns false when the stream is exhausted with no further <si> tags.
    private static bool FindNextSiCandidate(ScanBuffer buf)
    {
        while (true)
        {
            if (buf.IsExhausted)
            {
                return false;
            }
            ReadOnlySpan<byte> span = buf.Span;
            if (span.IsEmpty)
            {
                buf.Refill();
                continue;
            }
            int siIdx = span.IndexOf("si"u8);
            if (siIdx < 0)
            {
                int keep = Math.Min(ContextKeep, span.Length);
                buf.Advance(span.Length - keep);
                if (buf.CanReadMore)
                {
                    buf.Refill();
                    continue;
                }
                return false;
            }
            // With ContextKeep bytes preserved, valid '<si' will have siIdx >= 1.
            if (siIdx == 0)
            {
                buf.Advance(1);
                continue;
            }
            if (!CheckBackwardContext(span, siIdx))
            {
                buf.Advance(siIdx + 2);
                continue;
            }
            int afterSiIdx = siIdx + 2;
            if (afterSiIdx >= span.Length)
            {
                byte before = span[siIdx - 1];
                int ltOffset = (before == (byte)':') ? FindLtOffset(span, siIdx) : siIdx - 1;
                buf.Advance(ltOffset);
                buf.Refill();
                continue;
            }
            if (!IsTagNameBoundary(span[afterSiIdx]))
            {
                buf.Advance(siIdx + 2);
                continue;
            }
            buf.Advance(siIdx + 2);
            return true;
        }
    }

    // Returns true if the "si" at siIdx is preceded by '<' (direct) or '<prefix:' (namespaced).
    // Requires siIdx >= 1.
    private static bool CheckBackwardContext(ReadOnlySpan<byte> span, int siIdx)
    {
        byte before = span[siIdx - 1];
        if (before == (byte)'<')
        {
            return true;
        }
        if (before == (byte)':')
        {
            int k = siIdx - 2;
            while (k >= 0 && IsValidPrefixChar(span[k]))
            {
                k--;
            }
            return k >= 0 && span[k] == (byte)'<';
        }
        return false;
    }

    // Advances past the '>' (or '/>') that closes the current opening tag.
    // Returns true when the element is empty (/>), false for a normal open tag (>).
    private static bool SkipOpeningTagClose(ScanBuffer buf)
    {
        while (true)
        {
            if (buf.IsExhausted)
            {
                return false;
            }
            ReadOnlySpan<byte> span = buf.Span;
            if (span.IsEmpty)
            {
                buf.Refill();
                continue;
            }
            int idx = span.IndexOfAny((byte)'>', (byte)'/');
            if (idx < 0)
            {
                buf.Advance(span.Length);
                buf.Refill();
                continue;
            }
            if (span[idx] == (byte)'/')
            {
                if (TryConsumeEmptyClose(buf, span, idx))
                {
                    return true;
                }
                continue;
            }
            buf.Advance(idx + 1);
            return false;
        }
    }

    // Handles a '/' byte encountered while scanning an opening tag.
    // Returns true if '/>' was consumed (empty element), false to keep scanning.
    private static bool TryConsumeEmptyClose(ScanBuffer buf, ReadOnlySpan<byte> span, int idx)
    {
        int nextIdx = idx + 1;
        if (nextIdx < span.Length)
        {
            if (span[nextIdx] == (byte)'>')
            {
                buf.Advance(nextIdx + 1);
                return true;
            }
            // '/' inside an attribute value (rare) — skip it and keep scanning.
            buf.Advance(nextIdx);
            return false;
        }
        // '/' is the last byte; peek into the next buffer chunk.
        buf.Advance(idx);
        buf.Refill();
        span = buf.Span;
        if (!span.IsEmpty && span[0] == (byte)'/')
        {
            if (span.Length > 1 && span[1] == (byte)'>')
            {
                buf.Advance(2);
                return true;
            }
            buf.Advance(1);
        }
        return false;
    }

    // Handles the confirmed <si…> open tag: records empty element or delegates to content scan.
    private static void DispatchSiElement(
        ScanBuffer buf,
        ref byte[] arenaBuf,
        ref int arenaOffset,
        ref long[] infoBuf,
        ref int infoCount)
    {
        bool isEmpty = SkipOpeningTagClose(buf);
        EnsureInfoCapacity(ref infoBuf, infoCount);
        if (isEmpty)
        {
            infoBuf[infoCount++] = (long)arenaOffset << 32;
        }
        else
        {
            ProcessSiContent(buf, ref arenaBuf, ref arenaOffset, ref infoBuf, ref infoCount);
        }
    }

    // Phase 2: sequential scan inside a <si> block for <t> content.
    private static void ProcessSiContent(
        ScanBuffer buf,
        ref byte[] arenaBuf,
        ref int arenaOffset,
        ref long[] infoBuf,
        ref int infoCount)
    {
        int entryStart = arenaOffset;
        while (true)
        {
            if (buf.IsExhausted)
            {
                break;
            }
            ReadOnlySpan<byte> span = buf.Span;
            if (span.IsEmpty)
            {
                buf.Refill();
                continue;
            }
            int ltIdx = span.IndexOf((byte)'<');
            if (ltIdx < 0)
            {
                buf.Advance(span.Length);
                if (buf.CanReadMore)
                {
                    buf.Refill();
                }
                continue;
            }
            if (ltIdx + 1 >= span.Length)
            {
                buf.Advance(ltIdx);
                buf.Refill();
                continue;
            }
            byte next = span[ltIdx + 1];
            if (next == (byte)'t')
            {
                TryHandleTTag(buf, span, ltIdx, ref arenaBuf, ref arenaOffset);
            }
            else if (next == (byte)'/')
            {
                if (HandleClosingTag(buf, span, ltIdx))
                {
                    break;
                }
            }
            else
            {
                buf.Advance(ltIdx + 1);
            }
        }
        int length = arenaOffset - entryStart;
        EnsureInfoCapacity(ref infoBuf, infoCount);
        infoBuf[infoCount++] = ((long)entryStart << 32) | (uint)length;
    }

    // Handles a potential <t…> open tag at ltIdx. Advances the buffer and copies content
    // when confirmed; otherwise advances past the non-<t> bytes and returns.
    private static void TryHandleTTag(
        ScanBuffer buf,
        ReadOnlySpan<byte> span,
        int ltIdx,
        ref byte[] arenaBuf,
        ref int arenaOffset)
    {
        if (ltIdx + 2 >= span.Length)
        {
            buf.Advance(ltIdx);
            buf.Refill();
            return;
        }
        if (!IsTagNameBoundary(span[ltIdx + 2]))
        {
            buf.Advance(ltIdx + 2);
            return;
        }
        // Confirmed <t…>: advance past '<t', skip to closing '>', copy content, skip '</t>'.
        buf.Advance(ltIdx + 2);
        SkipToGt(buf);
        CopyTextContent(buf, ref arenaBuf, ref arenaOffset);
        SkipToGt(buf);
    }

    // Handles a potential close tag starting at span[ltIdx] (already known to be '</…>').
    // Returns true when </si> is confirmed and the <si> block is complete.
    private static bool HandleClosingTag(ScanBuffer buf, ReadOnlySpan<byte> span, int ltIdx)
    {
        // Need enough bytes to distinguish </si> from </r>, </rPr>, etc.
        // Minimum for '</si>' is 5 bytes; refill when we have fewer.
        if (ltIdx + 4 >= span.Length && buf.CanReadMore)
        {
            buf.Advance(ltIdx);
            buf.Refill();
            return false;
        }
        if (IsCloseSiTag(span, ltIdx))
        {
            buf.Advance(ltIdx + 1);
            SkipToGt(buf);
            return true;
        }
        buf.Advance(ltIdx + 1);
        return false;
    }

    // Checks whether span[ltIdx] starts '</si' (or '</prefix:si').
    private static bool IsCloseSiTag(ReadOnlySpan<byte> span, int ltIdx)
    {
        int pos = ltIdx + 2;
        if (pos >= span.Length)
        {
            return false;
        }
        int nameStart = pos;
        for (int i = pos; i < Math.Min(pos + 12, span.Length); i++)
        {
            if (span[i] == (byte)':')
            {
                nameStart = i + 1;
                break;
            }
            if (!IsValidPrefixChar(span[i]))
            {
                break;
            }
        }
        if (nameStart + 1 >= span.Length)
        {
            return false;
        }
        if (span[nameStart] != (byte)'s' || span[nameStart + 1] != (byte)'i')
        {
            return false;
        }
        int boundaryPos = nameStart + 2;
        if (boundaryPos >= span.Length)
        {
            return false;
        }
        return IsTagNameBoundary(span[boundaryPos]);
    }

    // Copies raw bytes into the arena until '<' is found.
    // Leaves the buffer positioned AT the '<' (does not consume it).
    private static void CopyTextContent(ScanBuffer buf, ref byte[] arenaBuf, ref int arenaOffset)
    {
        while (true)
        {
            ReadOnlySpan<byte> span = buf.Span;
            if (span.IsEmpty)
            {
                if (buf.CanReadMore)
                {
                    buf.Refill();
                    continue;
                }
                return;
            }
            int ltIdx = span.IndexOf((byte)'<');
            if (ltIdx >= 0)
            {
                if (ltIdx > 0)
                {
                    EnsureArenaCapacity(ref arenaBuf, arenaOffset, ltIdx);
                    span.Slice(0, ltIdx).CopyTo(arenaBuf.AsSpan(arenaOffset));
                    arenaOffset += ltIdx;
                }
                buf.Advance(ltIdx);
                return;
            }
            EnsureArenaCapacity(ref arenaBuf, arenaOffset, span.Length);
            span.CopyTo(arenaBuf.AsSpan(arenaOffset));
            arenaOffset += span.Length;
            buf.Advance(span.Length);
            if (buf.CanReadMore)
            {
                buf.Refill();
            }
        }
    }

    // Advances the buffer past the next '>' character (end of any tag).
    private static void SkipToGt(ScanBuffer buf)
    {
        while (true)
        {
            ReadOnlySpan<byte> span = buf.Span;
            int gtIdx = span.IndexOf((byte)'>');
            if (gtIdx >= 0)
            {
                buf.Advance(gtIdx + 1);
                return;
            }
            if (buf.IsExhausted)
            {
                return;
            }
            buf.Advance(span.Length);
            buf.Refill();
        }
    }

    // Returns the span offset of the '<' that starts a namespace-prefixed tag ending at siIdx.
    private static int FindLtOffset(ReadOnlySpan<byte> span, int siIdx)
    {
        int k = siIdx - 2;
        while (k >= 0 && IsValidPrefixChar(span[k]))
        {
            k--;
        }
        return k >= 0 ? k : 0;
    }

    private static void EnsureArenaCapacity(ref byte[] arenaBuf, int used, int needed)
    {
        if (used + needed <= arenaBuf.Length)
        {
            return;
        }
        int newSize = Math.Max(arenaBuf.Length * 2, used + needed);
        var bigger = ArrayPool<byte>.Shared.Rent(newSize);
        arenaBuf.AsSpan(0, used).CopyTo(bigger);
        ArrayPool<byte>.Shared.Return(arenaBuf, clearArray: false);
        arenaBuf = bigger;
    }

    private static void EnsureInfoCapacity(ref long[] infoBuf, int used)
    {
        if (used < infoBuf.Length)
        {
            return;
        }
        var bigger = ArrayPool<long>.Shared.Rent(infoBuf.Length * 2);
        infoBuf.AsSpan(0, used).CopyTo(bigger);
        ArrayPool<long>.Shared.Return(infoBuf, clearArray: false);
        infoBuf = bigger;
    }

    private static bool IsTagNameBoundary(byte b) =>
        b == (byte)'>' || b == (byte)'/' || b == (byte)' ' ||
        b == (byte)'\t' || b == (byte)'\r' || b == (byte)'\n';

    private static bool IsValidPrefixChar(byte b) =>
        (b >= (byte)'a' && b <= (byte)'z') ||
        (b >= (byte)'A' && b <= (byte)'Z') ||
        (b >= (byte)'0' && b <= (byte)'9') ||
        b == (byte)'_' || b == (byte)'-' || b == (byte)'.';
}
