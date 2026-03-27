using XLSight.SharedStrings;
using System.Buffers;
using System.Runtime.InteropServices;
using System.Text;
using XLSight.Models;
using XLSight.Models.Analysis;
using XLSight.Parsing;
using XLSight.Styles;
using XLSight.Worksheets;

namespace XLSight.ByteEngine;

/// <summary>
/// Streaming worksheet scanner that operates on raw decompressed UTF-8 bytes,
/// bypassing XmlReader entirely. Uses SIMD-accelerated ReadOnlySpan&lt;byte&gt;.IndexOf
/// via the runtime's vectorised implementation.
/// </summary>
internal static class XlsxSheetScanner
{
    internal static ReadOnlySpan<byte> TagSheetData => "sheetData"u8;
    internal static ReadOnlySpan<byte> TagRow => "row"u8;
    internal static ReadOnlySpan<byte> TagCell => "c"u8;
    internal static ReadOnlySpan<byte> TagValue => "v"u8;
    internal static ReadOnlySpan<byte> TagText => "t"u8;

    // SearchValues instances are pre-built bitmaps/SIMD vectors; AVX-512 on supported hardware.
    private static readonly SearchValues<byte> s_tagBoundaries = SearchValues.Create(">/ \t\r\n"u8);
    private static readonly SearchValues<byte> s_xmlWhitespace  = SearchValues.Create(" \t\r\n"u8);

    // ── Entry points ─────────────────────────────────────────────────────────

    /// <summary>
    /// Streams decoded <see cref="ExcelRow"/> values from a worksheet entry stream.
    /// Each yielded row owns its cells array — safe to materialise with .ToList().
    /// Equivalent in output to <c>WorksheetScanner.ScanRows</c> but operates on
    /// raw UTF-8 bytes rather than via <see cref="System.Xml.XmlReader"/>.
    /// </summary>
    internal static IEnumerable<ExcelRow> ScanRows(
        Stream entryStream,
        SharedStringTable sharedStrings,
        StyleTable styles,
        bool isDate1904,
        ExcelReadMode mode,
        ExcelRange range,
        long seekHint = -1)
    {
        using var buf = new ScanBuffer(entryStream);

        if (!SeekToSheetData(buf, entryStream, seekHint))
        {
            yield break;
        }

        var cellBuf = ArrayPool<ExcelCellValue>.Shared.Rent(ExcelLimits.MaxColumns);
        int lastRow = 0;

        try
        {
            while (true)
            {
                if (!TryReadRowStart(buf, ref lastRow)) { yield break; }

                int rowIndex = lastRow;
                if (!range.IsUnbounded && rowIndex > range.BottomRight.Row) { yield break; }
                if (!range.IsUnbounded && rowIndex < range.TopLeft.Row) { SkipToEndTag(buf, TagRow); continue; }

                if (FillRowCells(buf, rowIndex, sharedStrings, styles, isDate1904, range, cellBuf,
                    out int startCol, out int width))
                {
                    var cells = new ExcelCellValue[width];
                    cellBuf.AsSpan(0, width).CopyTo(cells);
                    cellBuf.AsSpan(0, width).Clear();
                    yield return new ExcelRow(rowIndex, cells, startCol);
                }
            }
        }
        finally
        {
            ArrayPool<ExcelCellValue>.Shared.Return(cellBuf, clearArray: false);
        }
    }

    internal static SheetCursor OpenCursor(
        Stream entryStream,
        SharedStringTable sharedStrings,
        StyleTable styles,
        bool isDate1904,
        ExcelReadMode mode,
        ExcelRange range,
        long seekHint = -1)
        => new(entryStream, sharedStrings, styles, isDate1904, range, seekHint);

    /// <summary>
    /// Push-based sheet scanner. Drives <paramref name="sink"/> for every decoded cell.
    /// Uses the same SIMD byte-scanning engine as <see cref="ScanRows"/> but avoids
    /// per-row array allocation by calling the sink directly instead of yielding rows.
    /// </summary>
    internal static void ScanSheet<TSink>(
        Stream entryStream,
        SharedStringTable sharedStrings,
        StyleTable styles,
        bool isDate1904,
        ExcelRange range,
        ref TSink sink,
        long seekHint = -1)
        where TSink : struct, IByteSheetSink
    {
        using var buf = new ScanBuffer(entryStream);

        if (!SeekToSheetData(buf, entryStream, seekHint))
        {
            sink.OnEnd();
            return;
        }

        int lastRow = 0;

        while (true)
        {
            if (!TryReadRowStart(buf, ref lastRow)) { break; }

            int rowIndex = lastRow;

            if (!range.IsUnbounded && rowIndex > range.BottomRight.Row) { break; }

            if (!range.IsUnbounded && rowIndex < range.TopLeft.Row)
            {
                SkipToEndTag(buf, TagRow);
                continue;
            }

            sink.OnRowStart(rowIndex);

            if (!PushRowCells(buf, rowIndex, sharedStrings, styles, isDate1904, range, ref sink))
            {
                break;
            }
        }

        // Scan for merge regions after </sheetData> (only relevant when range is unbounded,
        // i.e. Analyze calls; RangeReadSink ignores OnMergeCell).
        if (range.IsUnbounded)
        {
            TryScanMergeCells(buf, ref sink);
        }

        sink.OnEnd();
    }

    // ── Navigation helpers (internal so SheetCursor can call them) ──────────

    internal static bool SeekToSheetData(ScanBuffer buf, Stream stream, long seekHint)
    {
        if (seekHint >= 0 && stream.CanSeek)
        {
            stream.Seek(seekHint, SeekOrigin.Begin);
            buf.Reset();
            return true;
        }

        return ScanForSheetData(buf);
    }

    internal static bool ScanForSheetData(ScanBuffer buf)
    {
        while (true)
        {
            var span = buf.Span;
            var status = TryFindStartTag(span, TagSheetData, out var match, out int partialIndex);
            if (status == TagSearchResult.NotFound)
            {
                if (!RefillKeepingTagStart(buf, span, partialIndex)) { return false; }
                continue;
            }

            if (status == TagSearchResult.NeedMoreData)
            {
                buf.Advance(match.Start);
                if (!buf.Refill()) { return false; }
                continue;
            }

            if (match.IsEmptyElement) { return false; }

            buf.Advance(match.EndExclusive);
            return true;
        }
    }

    internal static bool TryReadRowStart(ScanBuffer buf, ref int rowIndex)
    {
        while (true)
        {
            var span = buf.Span;
            var closeStatus = TryFindEndTag(span, TagSheetData, out int closeIdx, out int closeLen, out int closePartial);
            var rowStatus = TryFindStartTag(span, TagRow, out var rowMatch, out int rowPartial);

            if (closeStatus == TagSearchResult.Found
                && (rowStatus != TagSearchResult.Found || closeIdx < rowMatch.Start))
            {
                return false;
            }

            if (rowStatus == TagSearchResult.NotFound)
            {
                if (!RefillKeepingTagStart(buf, span, MaxPartial(closePartial, rowPartial))) { return false; }
                continue;
            }

            if (rowStatus == TagSearchResult.NeedMoreData)
            {
                buf.Advance(rowMatch.Start);
                if (!buf.Refill()) { return false; }
                continue;
            }

            var attrBytes = span.Slice(rowMatch.AfterName, rowMatch.EndExclusive - rowMatch.AfterName);
            bool hasR = CellAttributeParser.ParseRowIndex(attrBytes, out int parsedRow);
            if (hasR && parsedRow > ExcelLimits.MaxRows)
            {
                buf.Advance(rowMatch.EndExclusive);
                SkipToEndTag(buf, TagRow);
                continue;
            }

            rowIndex = hasR && parsedRow > 0 ? parsedRow : rowIndex + 1;
            buf.Advance(rowMatch.EndExclusive);
            return true;
        }
    }

    internal static bool FillRowCells(
        ScanBuffer buf, int rowIndex, SharedStringTable sharedStrings, StyleTable styles,
        bool isDate1904, ExcelRange range, ExcelCellValue[] cellBuf,
        out int startCol, out int width)
    {
        startCol = 0;
        width = 0;
        int currentCol = 0;
        int firstCol = 0;
        int lastCol = 0;

        while (TryReadNextCell(buf, ref currentCol, out var kind, out int styleIdx, out bool isEmpty))
        {
            if (currentCol > ExcelLimits.MaxColumns) { if (!isEmpty) { SkipToEndTag(buf, TagCell); } continue; }

            bool inRange = range.IsUnbounded || range.Contains(new ExcelAddress(currentCol, rowIndex));
            if (!inRange) { if (!isEmpty) { SkipToEndTag(buf, TagCell); } continue; }

            if (firstCol == 0) { firstCol = currentCol; }

            cellBuf[currentCol - firstCol] = isEmpty
                ? ExcelCellValue.Empty
                : ReadCellValue(buf, kind, styleIdx, sharedStrings, styles, isDate1904);

            if (currentCol > lastCol) { lastCol = currentCol; }
        }

        if (firstCol == 0) { return false; }

        startCol = firstCol;
        width = lastCol - firstCol + 1;
        return true;
    }

    private static bool TryReadNextCell(
        ScanBuffer buf, ref int col,
        out CellDataKind kind, out int styleIdx, out bool isEmpty)
    {
        kind = CellDataKind.Number;
        styleIdx = 0;
        isEmpty = false;

        while (true)
        {
            var span = buf.Span;
            var closeStatus = TryFindEndTag(span, TagRow, out int closeRow, out int closeRowLen, out int closePartial);
            var cellStatus = TryFindStartTag(span, TagCell, out var cellMatch, out int cellPartial);

            if (closeStatus == TagSearchResult.Found
                && (cellStatus != TagSearchResult.Found || closeRow < cellMatch.Start))
            {
                buf.Advance(closeRow + closeRowLen);
                return false;
            }

            if (cellStatus == TagSearchResult.NotFound)
            {
                if (!RefillKeepingTagStart(buf, span, MaxPartial(closePartial, cellPartial))) { return false; }
                continue;
            }

            if (cellStatus == TagSearchResult.NeedMoreData)
            {
                buf.Advance(cellMatch.Start);
                if (!buf.Refill()) { return false; }
                continue;
            }

            var attrBytes = span.Slice(cellMatch.AfterName, cellMatch.EndExclusive - cellMatch.AfterName);
            CellAttributeParser.ParseCellAttrs(attrBytes, out int parsedCol, out _, out kind, out styleIdx, out isEmpty);
            col = parsedCol > 0 ? parsedCol : col + 1;
            buf.Advance(cellMatch.EndExclusive);
            return true;
        }
    }

    // ── Cell value readers ──────────────────────────────────────────────────

    internal static ExcelCellValue ReadCellValue(
        ScanBuffer buf, CellDataKind kind, int styleIdx,
        SharedStringTable sharedStrings, StyleTable styles, bool isDate1904)
    {
        if (kind == CellDataKind.InlineString) { return ReadInlineString(buf); }

        while (true)
        {
            var span = buf.Span;
            var valueStatus = TryFindStartTag(span, TagValue, out var valueMatch, out int valuePartial);
            var closeStatus = TryFindEndTag(span, TagCell, out int cClose, out int cCloseLen, out int closePartial);

            if (closeStatus == TagSearchResult.Found
                && (valueStatus != TagSearchResult.Found || cClose < valueMatch.Start))
            {
                buf.Advance(cClose + cCloseLen);
                return ExcelCellValue.Empty;
            }

            if (valueStatus == TagSearchResult.NotFound)
            {
                if (!RefillKeepingTagStart(buf, span, MaxPartial(valuePartial, closePartial))) { return ExcelCellValue.Empty; }
                continue;
            }

            if (valueStatus == TagSearchResult.NeedMoreData)
            {
                buf.Advance(valueMatch.Start);
                if (!buf.Refill()) { return ExcelCellValue.Empty; }
                continue;
            }

            buf.Advance(valueMatch.EndExclusive);
            if (valueMatch.IsEmptyElement)
            {
                SkipToEndTag(buf, TagCell);
                return ExcelCellValue.Empty;
            }
            break;
        }

        var value = ExtractUntilClose(buf, TagValue);
        SkipToEndTag(buf, TagCell);
        return Utf8CellDecoder.Decode(value, kind, styleIdx, sharedStrings, styles, isDate1904);
    }

    internal static ExcelCellValue ReadInlineString(ScanBuffer buf)
    {
        bool seenT = false;
        string? first = null;
        StringBuilder? sb = null;

        while (true)
        {
            var span = buf.Span;
            var closeStatus = TryFindEndTag(span, TagCell, out int cClose, out int cCloseLen, out int closePartial);
            var textStatus = TryFindStartTag(span, TagText, out var textMatch, out int textPartial);

            if (closeStatus == TagSearchResult.Found
                && (textStatus != TagSearchResult.Found || cClose < textMatch.Start))
            {
                buf.Advance(cClose + cCloseLen);
                break;
            }

            if (textStatus == TagSearchResult.NotFound)
            {
                if (!RefillKeepingTagStart(buf, span, MaxPartial(closePartial, textPartial))) { break; }
                continue;
            }

            if (textStatus == TagSearchResult.NeedMoreData)
            {
                buf.Advance(textMatch.Start);
                if (!buf.Refill()) { break; }
                continue;
            }

            seenT = true;
            buf.Advance(textMatch.EndExclusive);
            if (textMatch.IsEmptyElement) { continue; }

            var textBytes = ExtractUntilClose(buf, TagText);
            if (!textBytes.IsEmpty) { AccumulateInlineText(textBytes, ref first, ref sb); }
        }

        var result = sb?.ToString() ?? first;
        if (result is not null) { return ExcelCellValue.FromText(result); }
        return seenT ? ExcelCellValue.FromText(string.Empty) : ExcelCellValue.Empty;
    }

    // ── Buffer helpers ──────────────────────────────────────────────────────

    // ── Push-based helpers for ScanSheet<TSink> ─────────────────────────────

    private static ReadOnlySpan<byte> TagMergeCells => "mergeCells"u8;
    private static ReadOnlySpan<byte> TagMergeCell  => "mergeCell"u8;

    /// <summary>
    /// Pushes all cells in the current row to <paramref name="sink"/>.
    /// Returns false when the sink signals early termination.
    /// Mirrors the filtering logic in <see cref="FillRowCells"/>.
    /// </summary>
    private static bool PushRowCells<TSink>(
        ScanBuffer buf,
        int rowIndex,
        SharedStringTable sharedStrings,
        StyleTable styles,
        bool isDate1904,
        ExcelRange range,
        ref TSink sink)
        where TSink : struct, IByteSheetSink
    {
        int currentCol = 0;

        while (TryReadNextCell(buf, ref currentCol, out var kind, out int styleIdx, out bool isEmpty))
        {
            if (currentCol > ExcelLimits.MaxColumns)
            {
                if (!isEmpty) { SkipToEndTag(buf, TagCell); }
                continue;
            }

            bool inRange = range.IsUnbounded || range.Contains(new ExcelAddress(currentCol, rowIndex));
            if (!inRange)
            {
                if (!isEmpty) { SkipToEndTag(buf, TagCell); }
                continue;
            }

            ExcelCellValue value = isEmpty
                ? ExcelCellValue.Empty
                : ReadCellValue(buf, kind, styleIdx, sharedStrings, styles, isDate1904);

            if (!sink.OnCell(currentCol, kind, styleIdx, value))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Scans bytes remaining after <c>&lt;/sheetData&gt;</c> for <c>&lt;mergeCell&gt;</c>
    /// elements and calls <see cref="IByteSheetSink.OnMergeCell"/> for each one found.
    /// Stops at <c>&lt;/mergeCells&gt;</c> or EOF.
    /// </summary>
    private static void TryScanMergeCells<TSink>(ScanBuffer buf, ref TSink sink)
        where TSink : struct, IByteSheetSink
    {
        Span<char> refCharBuf = stackalloc char[32];

        while (true)
        {
            var span = buf.Span;

            // Bail out early when we hit </mergeCells>.
            var closeStatus = TryFindEndTag(span, TagMergeCells, out int closeIdx, out int closeLen, out int closePartial);
            var mergeStatus = TryFindStartTag(span, TagMergeCell, out var mergeMatch, out int mergePartial);

            if (closeStatus == TagSearchResult.Found
                && (mergeStatus != TagSearchResult.Found || closeIdx < mergeMatch.Start))
            {
                break;
            }

            if (mergeStatus == TagSearchResult.NotFound)
            {
                if (!RefillKeepingTagStart(buf, span, MaxPartial(closePartial, mergePartial))) { break; }
                continue;
            }

            if (mergeStatus == TagSearchResult.NeedMoreData)
            {
                buf.Advance(mergeMatch.Start);
                if (!buf.Refill()) { break; }
                continue;
            }

            // Extract and parse the ref="A1:B2" attribute.
            var attrBytes = span.Slice(mergeMatch.AfterName, mergeMatch.EndExclusive - mergeMatch.AfterName);
            if (CellAttributeParser.TryGetRefAttribute(attrBytes, out var refBytes))
            {
                int charLen = Encoding.UTF8.GetChars(refBytes, refCharBuf);
                if (AddressParser.TryParse(refCharBuf[..charLen], out var mergeRange))
                {
                    sink.OnMergeCell(new ExcelMergedRegion(
                        mergeRange.TopLeft.Row, mergeRange.TopLeft.Column,
                        mergeRange.BottomRight.Row, mergeRange.BottomRight.Column));
                }
            }

            buf.Advance(mergeMatch.EndExclusive);
        }
    }

    internal static ReadOnlySpan<byte> ExtractUntilClose(ScanBuffer buf, ReadOnlySpan<byte> tagName)
    {
        while (true)
        {
            var span = buf.Span;
            var status = TryFindEndTag(span, tagName, out int closeIdx, out int closeLen, out int partialIndex);
            if (status == TagSearchResult.Found)
            {
                var value = span[..closeIdx];
                buf.Advance(closeIdx + closeLen);
                return value;
            }

            if (status == TagSearchResult.NeedMoreData)
            {
                if (closeIdx >= 0)
                {
                    buf.Advance(closeIdx);
                    if (!buf.Refill()) { return ReadOnlySpan<byte>.Empty; }
                }
                else if (!RefillKeepingTagStart(buf, span, partialIndex)) { return ReadOnlySpan<byte>.Empty; }
                continue;
            }

            if (!RefillKeepingTagStart(buf, span, partialIndex)) { return ReadOnlySpan<byte>.Empty; }
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
                    if (!buf.Refill()) { return; }
                }
                else if (!RefillKeepingTagStart(buf, span, partialIndex)) { return; }
                continue;
            }

            if (!RefillKeepingTagStart(buf, span, partialIndex)) { return; }
        }
    }

    private static bool RefillSkipping(ScanBuffer buf, ReadOnlySpan<byte> span, int overlap)
    {
        int safe = span.Length - overlap;
        buf.Advance(safe > 0 ? safe : span.Length);
        return buf.Refill();
    }

    private static bool RefillKeepingTagStart(ScanBuffer buf, ReadOnlySpan<byte> span, int partialIndex)
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

        return RefillSkipping(buf, span, overlap: 0);
    }

    private static TagSearchResult TryFindStartTag(
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
            int idx = span[search..].IndexOf(localName);
            if (idx < 0) { return TagSearchResult.NotFound; }

            idx += search;
            int nameEnd = idx + localName.Length;

            if (nameEnd >= span.Length)
            {
                partialIndex = -1; // RefillKeepingTagStart will find last '<'
                return TagSearchResult.NeedMoreData;
            }

            byte boundary = span[nameEnd];
            if (!IsTagNameBoundary(boundary))
            {
                search = idx + 1;
                continue;
            }

            if (!TryGetOpenTagStart(span, idx, out int tagStart))
            {
                if (tagStart == NeedMoreDataSentinel) { partialIndex = -1; return TagSearchResult.NeedMoreData; }
                search = idx + 1;
                continue;
            }

            int gt = span[nameEnd..].IndexOf((byte)'>');
            if (gt < 0)
            {
                match = new StartTagMatch(tagStart, nameEnd, 0, false);
                partialIndex = tagStart;
                return TagSearchResult.NeedMoreData;
            }

            int endExclusive = nameEnd + gt + 1;
            match = new StartTagMatch(tagStart, nameEnd, endExclusive, IsSelfClosing(span, nameEnd, endExclusive - 1));
            return TagSearchResult.Found;
        }
    }

    private static TagSearchResult TryFindEndTag(
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
            int idx = span[search..].IndexOf(localName);
            if (idx < 0) { return TagSearchResult.NotFound; }

            idx += search;
            int nameEnd = idx + localName.Length;

            if (nameEnd >= span.Length) { partialIndex = -1; return TagSearchResult.NeedMoreData; }

            int closeGt = FindCloseAngleBracket(span, nameEnd);
            if (closeGt == -2) { search = idx + 1; continue; }
            if (closeGt == -1) { partialIndex = -1; return TagSearchResult.NeedMoreData; }

            if (!TryGetCloseTagStart(span, idx, out tagStart))
            {
                if (tagStart == NeedMoreDataSentinel) { tagStart = -1; partialIndex = -1; return TagSearchResult.NeedMoreData; }
                tagStart = -1;
                search = idx + 1;
                continue;
            }

            tagLength = closeGt - tagStart + 1;
            return TagSearchResult.Found;
        }
    }

    /// <summary>
    /// Starting at <paramref name="cursor"/>, skips optional whitespace and returns the
    /// index of the closing '&gt;'. Returns -1 if span is exhausted (need more data),
    /// or -2 if a non-whitespace/non-'&gt;' char is found (not a valid end tag).
    /// </summary>
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

    private const int NeedMoreDataSentinel = int.MinValue;

    /// <summary>
    /// Looks backward from <paramref name="nameIdx"/> to confirm a start tag opening.
    /// Sets <paramref name="tagStart"/> to the '&lt;' index on success.
    /// Returns false with <see cref="NeedMoreDataSentinel"/> if there is not enough preceding context.
    /// </summary>
    private static bool TryGetOpenTagStart(ReadOnlySpan<byte> span, int nameIdx, out int tagStart)
    {
        tagStart = NeedMoreDataSentinel;
        if (nameIdx == 0) { return false; }

        byte before = span[nameIdx - 1];
        if (before == (byte)'<') { tagStart = nameIdx - 1; return true; }

        if (before == (byte)':')
        {
            int i = nameIdx - 2;
            while (i >= 0 && IsValidPrefixChar(span[i])) { i--; }
            if (i < 0) { return false; } // need more context
            if (span[i] == (byte)'<') { tagStart = i; return true; }
        }

        tagStart = -2;
        return false;
    }

    /// <summary>
    /// Looks backward from <paramref name="nameIdx"/> to confirm an end tag opening.
    /// Sets <paramref name="tagStart"/> to the '&lt;' index on success.
    /// Returns false with <see cref="NeedMoreDataSentinel"/> if there is not enough preceding context.
    /// </summary>
    private static bool TryGetCloseTagStart(ReadOnlySpan<byte> span, int nameIdx, out int tagStart)
    {
        tagStart = NeedMoreDataSentinel;
        if (nameIdx < 2) { return false; }

        byte before = span[nameIdx - 1];
        if (before == (byte)'/' && span[nameIdx - 2] == (byte)'<') { tagStart = nameIdx - 2; return true; }

        if (before == (byte)':')
        {
            int i = nameIdx - 2;
            while (i >= 0 && IsValidPrefixChar(span[i])) { i--; }
            if (i < 1) { return false; } // need more context
            if (span[i] == (byte)'/' && span[i - 1] == (byte)'<') { tagStart = i - 1; return true; }
        }

        tagStart = -2;
        return false;
    }

    private static bool IsSelfClosing(ReadOnlySpan<byte> span, int attrStart, int gtIndex)
    {
        for (int i = gtIndex - 1; i >= attrStart; i--)
        {
            byte ch = span[i];
            if (IsXmlWhitespace(ch)) { continue; }
            return ch == (byte)'/';
        }

        return false;
    }

    private static bool IsTagNameBoundary(byte ch) => s_tagBoundaries.Contains(ch);

    private static bool IsXmlWhitespace(byte ch) => s_xmlWhitespace.Contains(ch);

    private static bool IsValidPrefixChar(byte ch) =>
        ch is >= (byte)'a' and <= (byte)'z'
            or >= (byte)'A' and <= (byte)'Z'
            or >= (byte)'0' and <= (byte)'9'
            or (byte)'_'
            or (byte)'-'
            or (byte)'.';

    private static int MaxPartial(int left, int right) => left >= right ? left : right;

    private static void AccumulateInlineText(
        ReadOnlySpan<byte> bytes, ref string? first, ref StringBuilder? sb)
    {
        var cell = Utf8CellDecoder.Decode(bytes, CellDataKind.InlineString, 0, SharedStringTable.Empty, StyleTable.Default, false);
        if (cell.CellType != ExcelCellType.Text)
        {
            return;
        }

        var text = cell.AsText();
        if (first is null)
        {
            first = text;
        }
        else
        {
            (sb ??= new StringBuilder(first)).Append(text);
        }
    }

    private enum TagSearchResult
    {
        NotFound,
        Found,
        NeedMoreData,
    }

    [StructLayout(LayoutKind.Auto)]
    private readonly struct StartTagMatch(int start, int afterName, int endExclusive, bool isEmptyElement)
    {
        internal int Start { get; } = start;
        internal int AfterName { get; } = afterName;
        internal int EndExclusive { get; } = endExclusive;
        internal bool IsEmptyElement { get; } = isEmptyElement;
    }
}
