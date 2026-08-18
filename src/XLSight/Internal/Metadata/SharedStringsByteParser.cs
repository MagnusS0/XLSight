using System.Buffers;
using System.Text;
using XLSight.Internal.Readers.Xlsx;

namespace XLSight.Internal.Metadata;

/// <summary>
/// Allocation-free SST parser that scans raw UTF-8 bytes instead of using XmlReader.
/// Each <c>&lt;si&gt;</c> entry is assembled in a 256 KB staging buffer, then committed
/// atomically to a 64 KB arena chunk (below the LOH threshold). XML entities are
/// resolved into the staging buffer so the arena always contains clean UTF-8.
/// </summary>
internal static class SharedStringsByteParser
{
    // 256 KB covers Excel's maximum cell size (32 767 chars ≈ 131 KB UTF-8) with margin.
    private const int StagingCapacity = 256 * 1024;

    private const int ContextKeep = 20;

    /// <summary>
    /// Creates a lazy <see cref="SharedStringTable"/> that owns <paramref name="stream"/>
    /// and pumps it on demand. The stream is closed once all entries are parsed or when
    /// the table is disposed.
    /// </summary>
    internal static SharedStringTable Parse(Stream stream)
    {
        byte[] stagingBuf = ArrayPool<byte>.Shared.Rent(StagingCapacity);
        var buf = new ScanBuffer(stream); // ownership transferred to SharedStringTable
        int declaredUniqueCount = ReadDeclaredUniqueCount(buf.Span);
        var state = new ParseState(stagingBuf, declaredUniqueCount);
        return new SharedStringTable(buf, state, stagingBuf, declaredUniqueCount);
    }

    // ── Parse state ───────────────────────────────────────────────────────────

    internal sealed class ParseState
    {
        public const int ArenaChunkSize = 65536; // 64 KB — below LOH threshold
        public const int InfoChunkSize  = 8192;  // 8 192 × 8 bytes = 64 KB

        public readonly List<byte[]> ArenaChunks = [new byte[ArenaChunkSize]];
        public readonly List<long[]> InfoChunks;
        public int ArenaChunkIdx;
        public int ArenaChunkOffset;
        public int InfoChunkIdx;
        public int InfoChunkOffset;
        public int TotalStrings;

        public readonly byte[] StagingBuf;
        public int StagingLen;

        public ParseState(byte[] stagingBuf, int declaredUniqueCount)
        {
            StagingBuf = stagingBuf;
            InfoChunks = [new long[GetInitialInfoChunkLength(declaredUniqueCount)]];
        }

        /// <summary>
        /// Atomically places the staging content into the arena and records the info entry.
        /// Because the string is fully assembled before this call, it is guaranteed to land
        /// contiguously inside one chunk — no cross-chunk string split is possible.
        /// </summary>
        public void CommitEntry()
        {
            int length = StagingLen;
            StagingLen = 0;

            // Move to a new arena chunk if the string does not fit.
            // Strings larger than one chunk (> 64 KB) are extremely rare in valid XLSX
            // but are handled by allocating a dedicated oversized chunk.
            if (ArenaChunkOffset + length > ArenaChunkSize)
            {
                ArenaChunkIdx++;
                ArenaChunkOffset = 0;
                ArenaChunks.Add(new byte[Math.Max(ArenaChunkSize, length)]);
            }

            if (length > 0)
            {
                StagingBuf.AsSpan(0, length).CopyTo(ArenaChunks[ArenaChunkIdx].AsSpan(ArenaChunkOffset));
            }

            // Global offset encodes chunk position: (chunkIdx << 16) | chunkOffset.
            // Max arena: 65 535 chunks × 64 KB ≈ 4 GB — sufficient for any real workbook.
            int globalOffset = (ArenaChunkIdx << 16) | ArenaChunkOffset;
            long packed      = ((long)globalOffset << 32) | (uint)length;

            EnsureInfoCapacity();

            InfoChunks[InfoChunkIdx][InfoChunkOffset++] = packed;
            ArenaChunkOffset += length;
            TotalStrings++;
        }

        private void EnsureInfoCapacity()
        {
            long[] currentChunk = InfoChunks[InfoChunkIdx];
            if (InfoChunkOffset < currentChunk.Length)
            {
                return;
            }

            if (currentChunk.Length < InfoChunkSize)
            {
                var grownChunk = new long[InfoChunkSize];
                currentChunk.AsSpan().CopyTo(grownChunk);
                InfoChunks[InfoChunkIdx] = grownChunk;
                return;
            }

            InfoChunkIdx++;
            InfoChunks.Add(new long[InfoChunkSize]);
            InfoChunkOffset = 0;
        }
    }

    private static int GetInitialInfoChunkLength(int declaredUniqueCount)
    {
        if (declaredUniqueCount < 0)
        {
            return ParseState.InfoChunkSize;
        }

        return Math.Min(declaredUniqueCount, ParseState.InfoChunkSize);
    }

    private static int ReadDeclaredUniqueCount(ReadOnlySpan<byte> span)
    {
        int tagStart = span.IndexOf("<sst"u8);
        if (tagStart < 0)
        {
            tagStart = span.IndexOf(":sst"u8);
        }

        if (tagStart < 0)
        {
            return -1;
        }

        int tagEnd = span[tagStart..].IndexOf((byte)'>');
        if (tagEnd < 0)
        {
            return -1;
        }

        ReadOnlySpan<byte> openingTag = span.Slice(tagStart, tagEnd + 1);
        int uniqueCountIndex = openingTag.IndexOf("uniqueCount=\""u8);
        if (uniqueCountIndex < 0)
        {
            return -1;
        }

        ReadOnlySpan<byte> valueBytes = openingTag[(uniqueCountIndex + "uniqueCount=\"".Length)..];
        int closingQuoteIndex = valueBytes.IndexOf((byte)'"');
        if (closingQuoteIndex < 0)
        {
            return -1;
        }

        valueBytes = valueBytes[..closingQuoteIndex];
        if (valueBytes.IsEmpty || !System.Buffers.Text.Utf8Parser.TryParse(valueBytes, out int uniqueCount, out int consumed) || consumed != valueBytes.Length)
        {
            return -1;
        }

        return uniqueCount;
    }

    // ── Phase 1: locate <si> elements ────────────────────────────────────────

    internal static bool FindNextSiCandidate(ScanBuffer buf)
    {
        while (true)
        {
            if (buf.IsExhausted) { return false; }

            ReadOnlySpan<byte> span = buf.Span;
            if (span.IsEmpty) { buf.Refill(); continue; }

            int siIdx = span.IndexOf("si"u8);
            if (siIdx < 0)
            {
                int keep = Math.Min(ContextKeep, span.Length);
                buf.Advance(span.Length - keep);
                if (buf.CanReadMore) { buf.Refill(); continue; }
                return false;
            }

            if (siIdx == 0) { buf.Advance(1); continue; }

            if (!CheckBackwardContext(span, siIdx)) { buf.Advance(siIdx + 2); continue; }

            int afterSi = siIdx + 2;
            if (afterSi >= span.Length)
            {
                byte before = span[siIdx - 1];
                int ltOff = (before == (byte)':') ? FindLtOffset(span, siIdx) : siIdx - 1;
                buf.Advance(ltOff);
                buf.Refill();
                continue;
            }

            if (!IsTagNameBoundary(span[afterSi])) { buf.Advance(siIdx + 2); continue; }

            buf.Advance(siIdx + 2);
            return true;
        }
    }

    private static bool CheckBackwardContext(ReadOnlySpan<byte> span, int siIdx)
    {
        byte before = span[siIdx - 1];
        if (before == (byte)'<') { return true; }
        if (before == (byte)':')
        {
            int k = siIdx - 2;
            while (k >= 0 && IsValidPrefixChar(span[k])) { k--; }
            return k >= 0 && span[k] == (byte)'<';
        }
        return false;
    }

    // ── Phase 2: dispatch <si> element ───────────────────────────────────────

    internal static void DispatchSiElement(ScanBuffer buf, ParseState state)
    {
        state.StagingLen = 0;
        bool isEmpty = SkipOpeningTagClose(buf);
        if (!isEmpty)
        {
            ProcessSiContent(buf, state);
        }
        state.CommitEntry();
    }

    // Scans past the rest of an opening tag (any element, not just <si>), returning
    // true if it closed as an empty element (.../>) rather than a plain '>'.
    private static bool SkipOpeningTagClose(ScanBuffer buf)
    {
        while (true)
        {
            if (buf.IsExhausted) { return false; }
            ReadOnlySpan<byte> span = buf.Span;
            if (span.IsEmpty) { buf.Refill(); continue; }

            int idx = span.IndexOfAny((byte)'>', (byte)'/');
            if (idx < 0) { buf.Advance(span.Length); buf.Refill(); continue; }

            if (span[idx] == (byte)'/')
            {
                if (TryConsumeEmptyClose(buf, span, idx)) { return true; }
                continue;
            }

            buf.Advance(idx + 1);
            return false;
        }
    }

    private static bool TryConsumeEmptyClose(ScanBuffer buf, ReadOnlySpan<byte> span, int idx)
    {
        int next = idx + 1;
        if (next < span.Length)
        {
            if (span[next] == (byte)'>') { buf.Advance(next + 1); return true; }
            buf.Advance(next);
            return false;
        }

        buf.Advance(idx);
        buf.Refill();
        span = buf.Span;
        if (!span.IsEmpty && span[0] == (byte)'/')
        {
            if (span.Length > 1 && span[1] == (byte)'>') { buf.Advance(2); return true; }
            buf.Advance(1);
        }
        return false;
    }

    // ── Phase 3: scan inside <si> for <t> content ────────────────────────────

    private static void ProcessSiContent(ScanBuffer buf, ParseState state)
    {
        while (true)
        {
            if (buf.IsExhausted) { break; }

            ReadOnlySpan<byte> span = buf.Span;
            if (span.IsEmpty) { buf.Refill(); continue; }

            int ltIdx = span.IndexOf((byte)'<');
            if (ltIdx < 0)
            {
                buf.Advance(span.Length);
                if (buf.CanReadMore) { buf.Refill(); }
                continue;
            }

            if (ltIdx + 1 >= span.Length) { buf.Advance(ltIdx); buf.Refill(); continue; }

            byte next = span[ltIdx + 1];
            if (next == (byte)'t')
            {
                TryHandleTTag(buf, span, ltIdx, state);
            }
            else if (next == (byte)'/')
            {
                if (HandleClosingTag(buf, span, ltIdx)) { break; }
            }
            else
            {
                buf.Advance(ltIdx + 1);
            }
        }
    }

    private static void TryHandleTTag(
        ScanBuffer buf, ReadOnlySpan<byte> span, int ltIdx, ParseState state)
    {
        if (ltIdx + 2 >= span.Length) { buf.Advance(ltIdx); buf.Refill(); return; }
        if (!IsTagNameBoundary(span[ltIdx + 2])) { buf.Advance(ltIdx + 2); return; }

        buf.Advance(ltIdx + 2);
        bool selfClosed = SkipOpeningTagClose(buf);
        if (selfClosed) { return; }

        CopyTextContent(buf, state);
        SkipToGt(buf);
    }

    private static bool HandleClosingTag(ScanBuffer buf, ReadOnlySpan<byte> span, int ltIdx)
    {
        TagSearchResult result = IsCloseSiTag(span, ltIdx);

        if (result == TagSearchResult.NeedMoreData)
        {
            if (!buf.CanReadMore)
            {
                buf.Advance(ltIdx + 1);
                return false;
            }

            buf.Advance(ltIdx);
            buf.Refill();
            return false;
        }

        if (result == TagSearchResult.Found)
        {
            buf.Advance(ltIdx + 1);
            SkipToGt(buf);
            return true;
        }

        buf.Advance(ltIdx + 1);
        return false;
    }

    // Scans the prefix up to whatever is actually buffered rather than a fixed cap,
    // so an unusually long namespace prefix just costs another refill instead of a
    // wrong verdict — the same class of bug as the self-closing <t/> fix.
    private static TagSearchResult IsCloseSiTag(ReadOnlySpan<byte> span, int ltIdx)
    {
        int pos = ltIdx + 2;
        if (pos >= span.Length) { return TagSearchResult.NeedMoreData; }

        int nameStart = pos;
        int i = pos;
        for (; i < span.Length; i++)
        {
            if (span[i] == (byte)':') { nameStart = i + 1; break; }
            if (!IsValidPrefixChar(span[i])) { break; }
        }

        if (i == span.Length) { return TagSearchResult.NeedMoreData; }
        if (nameStart + 1 >= span.Length) { return TagSearchResult.NeedMoreData; }
        if (span[nameStart] != (byte)'s' || span[nameStart + 1] != (byte)'i') { return TagSearchResult.NotFound; }

        int boundaryPos = nameStart + 2;
        if (boundaryPos >= span.Length) { return TagSearchResult.NeedMoreData; }
        return IsTagNameBoundary(span[boundaryPos]) ? TagSearchResult.Found : TagSearchResult.NotFound;
    }

    // ── Text content copy with inline entity resolution ───────────────────────

    // Copies text bytes into the staging buffer until '<' is found.
    // Leaves the buffer positioned AT the '<'.
    private static void CopyTextContent(ScanBuffer buf, ParseState state)
    {
        byte[] staging = state.StagingBuf;

        while (true)
        {
            ReadOnlySpan<byte> span = buf.Span;
            if (span.IsEmpty)
            {
                if (buf.CanReadMore) { buf.Refill(); continue; }
                return;
            }

            int stopIdx = span.IndexOfAny((byte)'<', (byte)'&');
            if (stopIdx < 0)
            {
                span.CopyTo(staging.AsSpan(state.StagingLen));
                state.StagingLen += span.Length;
                buf.Advance(span.Length);
                if (buf.CanReadMore) { buf.Refill(); }
                continue;
            }

            if (stopIdx > 0)
            {
                span.Slice(0, stopIdx).CopyTo(staging.AsSpan(state.StagingLen));
                state.StagingLen += stopIdx;
            }

            if (span[stopIdx] == (byte)'<')
            {
                buf.Advance(stopIdx);
                return;
            }

            // '&' — resolve entity and write decoded UTF-8 into staging.
            buf.Advance(stopIdx);
            ResolveEntity(buf, state);
        }
    }

    // Called with buf positioned AT '&'. Resolves the entity and writes to staging.
    private static void ResolveEntity(ScanBuffer buf, ParseState state)
    {
        buf.Advance(1); // skip '&'

        // Loop, not a single attempt: a partial read (e.g. DeflateStream/zlib-ng chunking)
        // can leave the buffer short of the full entity after only one refill.
        while (buf.Span.Length < 12 && buf.CanReadMore) { buf.Refill(); }

        var span   = buf.Span;
        var window = span.Length > 16 ? span.Slice(0, 16) : span;
        int semi   = window.IndexOf((byte)';');

        if (semi <= 0)
        {
            state.StagingBuf[state.StagingLen++] = (byte)'&';
            return;
        }

        var body = span.Slice(0, semi);

        if (body.Length > 0 && body[0] == (byte)'#')
        {
            WriteNumericRef(body.Slice(1), state.StagingBuf, ref state.StagingLen);
        }
        else if (TryDecodeNamedEntity(body, out byte ch))
        {
            state.StagingBuf[state.StagingLen++] = ch;
        }
        else
        {
            state.StagingBuf[state.StagingLen++] = (byte)'&';
            body.CopyTo(state.StagingBuf.AsSpan(state.StagingLen));
            state.StagingLen += body.Length;
            state.StagingBuf[state.StagingLen++] = (byte)';';
        }

        buf.Advance(semi + 1);
    }

    private static bool TryDecodeNamedEntity(ReadOnlySpan<byte> name, out byte ch)
    {
        if (name.SequenceEqual("amp"u8))  { ch = (byte)'&';  return true; }
        if (name.SequenceEqual("lt"u8))   { ch = (byte)'<';  return true; }
        if (name.SequenceEqual("gt"u8))   { ch = (byte)'>';  return true; }
        if (name.SequenceEqual("quot"u8)) { ch = (byte)'"';  return true; }
        if (name.SequenceEqual("apos"u8)) { ch = (byte)'\''; return true; }
        ch = 0;
        return false;
    }

    private static void WriteNumericRef(ReadOnlySpan<byte> digits, byte[] buf, ref int offset)
    {
        bool hex = digits.Length > 0 && (digits[0] == (byte)'x' || digits[0] == (byte)'X');
        ReadOnlySpan<byte> numPart = hex ? digits.Slice(1) : digits;

        int codepoint = 0;
        foreach (byte b in numPart)
        {
            if (b >= '0' && b <= '9') { codepoint = codepoint * (hex ? 16 : 10) + (b - '0'); }
            else if (hex && b >= 'a' && b <= 'f') { codepoint = codepoint * 16 + (b - 'a' + 10); }
            else if (hex && b >= 'A' && b <= 'F') { codepoint = codepoint * 16 + (b - 'A' + 10); }
            else { break; }
        }

        if (!Rune.TryCreate(codepoint, out Rune rune)) { return; }

        Span<byte> utf8 = stackalloc byte[4];
        int written = rune.EncodeToUtf8(utf8);
        utf8.Slice(0, written).CopyTo(buf.AsSpan(offset));
        offset += written;
    }

    // ── Shared helpers ────────────────────────────────────────────────────────

    private static void SkipToGt(ScanBuffer buf)
    {
        while (true)
        {
            ReadOnlySpan<byte> span = buf.Span;
            int gtIdx = span.IndexOf((byte)'>');
            if (gtIdx >= 0) { buf.Advance(gtIdx + 1); return; }
            if (buf.IsExhausted) { return; }
            buf.Advance(span.Length);
            buf.Refill();
        }
    }

    private static int FindLtOffset(ReadOnlySpan<byte> span, int siIdx)
    {
        int k = siIdx - 2;
        while (k >= 0 && IsValidPrefixChar(span[k])) { k--; }
        return k >= 0 ? k : 0;
    }

    private static bool IsTagNameBoundary(byte b) => XmlByteReader.IsTagNameBoundary(b);
    private static bool IsValidPrefixChar(byte b)  => XmlByteReader.IsValidPrefixChar(b);
}
