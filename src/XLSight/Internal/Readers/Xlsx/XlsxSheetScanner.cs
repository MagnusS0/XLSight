using System.Text;
using XLSight.Internal.Metadata;
using XLSight.Internal.Parsing;
using XLSight.Internal.Sinks;
using XLSight.Analysis;
using static XLSight.Internal.Readers.Xlsx.XmlByteReader;

namespace XLSight.Internal.Readers.Xlsx;

/// <summary>
/// Streaming worksheet scanner that operates on raw decompressed UTF-8 bytes,
/// bypassing XmlReader entirely. Uses SIMD-accelerated ReadOnlySpan&lt;byte&gt;.IndexOf
/// via the runtime's vectorised implementation.
/// </summary>
internal static partial class XlsxSheetScanner
{
    internal static ReadOnlySpan<byte> TagSheetData => "sheetData"u8;
    internal static ReadOnlySpan<byte> TagRow => "row"u8;
    internal static ReadOnlySpan<byte> TagCell => "c"u8;
    internal static ReadOnlySpan<byte> TagFormula => "f"u8;
    internal static ReadOnlySpan<byte> TagValue => "v"u8;
    internal static ReadOnlySpan<byte> TagText => "t"u8;

    private static ReadOnlySpan<byte> TagDimension => "dimension"u8;
    private static ReadOnlySpan<byte> RefAttr => "ref="u8;
    private static ReadOnlySpan<byte> TAttr => "t="u8;

    // ── Entry points ─────────────────────────────────────────────────────────

    internal static SheetCursor OpenCursor(
        Stream entryStream,
        SharedStringTable sharedStrings,
        StyleTable styles,
        bool isDate1904,
        ReadMode mode,
        ExcelRange range,
        long seekHint = -1)
        => new(entryStream, sharedStrings, styles, isDate1904, mode, range, seekHint);

    /// <summary>
    /// Push-based sheet scanner. Drives <paramref name="sink"/> for every decoded cell.
    /// Uses the same SIMD byte-scanning engine as <see cref="SheetCursor"/> but avoids
    /// per-row buffer management by calling the sink directly instead of yielding rows.
    /// </summary>
    internal static void ScanSheet<TSink>(
        Stream entryStream,
        SharedStringTable sharedStrings,
        StyleTable styles,
        bool isDate1904,
        ReadMode mode,
        ExcelRange range,
        ref TSink sink,
        long seekHint = -1)
        where TSink : struct, IByteSheetSink
    {
        using var buf = new ScanBuffer(entryStream);

        if (!SeekToSheetData(buf, entryStream, seekHint, out ExcelRange? dimension, out bool emptySheetData))
        {
            sink.OnEnd();
            return;
        }

        if (dimension.HasValue)
        {
            sink.OnDimension(dimension.Value);
        }

        int lastRow = 0;

        while (!emptySheetData)
        {
            if (!TryReadRowStart(buf, ref lastRow, out bool emptyRow)) { break; }
            if (emptyRow) { continue; }

            int rowIndex = lastRow;

            if (!range.IsUnbounded && rowIndex > range.BottomRight.Row) { break; }

            if (!range.IsUnbounded && rowIndex < range.TopLeft.Row)
            {
                SkipToEndTag(buf, TagRow);
                continue;
            }

            sink.OnRowStart(rowIndex);

            if (!PushRowCells(buf, rowIndex, sharedStrings, styles, isDate1904, mode, range, ref sink))
            {
                break;
            }
        }

        // Scan for merge regions, CF, DV, and hyperlinks after </sheetData>.
        // Only relevant for analysis sinks (range.IsUnbounded); RangeSink no-ops these callbacks.
        if (range.IsUnbounded)
        {
            TryScanPostSheetData(buf, ref sink);
        }

        sink.OnEnd();
    }

    // ── Navigation helpers (internal so SheetCursor can call them) ──────────

    internal static bool SeekToSheetData(ScanBuffer buf, Stream stream, long seekHint, out ExcelRange? dimension)
    {
        bool found = SeekToSheetData(buf, stream, seekHint, out dimension, out bool emptySheetData);
        return IsUsableSheetData(found, emptySheetData);
    }

    private static bool SeekToSheetData(
        ScanBuffer buf,
        Stream stream,
        long seekHint,
        out ExcelRange? dimension,
        out bool emptySheetData)
    {
        emptySheetData = false;
        if (seekHint >= 0 && stream.CanSeek)
        {
            stream.Seek(seekHint, SeekOrigin.Begin);
            buf.Reset();
            dimension = null;
            return true;
        }

        return ScanForSheetDataCore(buf, out dimension, out emptySheetData);
    }

    internal static bool ScanForSheetData(ScanBuffer buf, out ExcelRange? dimension)
    {
        bool found = ScanForSheetDataCore(buf, out dimension, out bool emptySheetData);
        return IsUsableSheetData(found, emptySheetData);
    }

    private static bool IsUsableSheetData(bool found, bool emptySheetData) => found && !emptySheetData;

    private static bool ScanForSheetDataCore(
        ScanBuffer buf,
        out ExcelRange? dimension,
        out bool emptySheetData)
    {
        dimension = null;
        emptySheetData = false;
        while (true)
        {
            var span = buf.Span;
            var dimStatus = TryFindStartTag(span, TagDimension, out var dimMatch, out int dimPartial);
            var sdStatus = TryFindStartTag(span, TagSheetData, out var sdMatch, out int sdPartial);
            bool dimFound = dimStatus == TagSearchResult.Found;
            bool sdFound = sdStatus == TagSearchResult.Found;
            int dimPos = dimFound ? dimMatch.Start : int.MaxValue;
            int sdPos = sdFound ? sdMatch.Start : int.MaxValue;
            int minPos = Math.Min(dimPos, sdPos);

            // If a NeedMoreData tag precedes the earliest found tag, advance and refill
            if (dimStatus == TagSearchResult.NeedMoreData && dimMatch.Start < minPos)
            {
                if (!RefillKeepingTagStart(buf, span, dimMatch.Start)) { return false; }
                continue;
            }

            if (sdStatus == TagSearchResult.NeedMoreData && sdMatch.Start < minPos)
            {
                if (!RefillKeepingTagStart(buf, span, sdMatch.Start)) { return false; }
                continue;
            }

            if (!dimFound && !sdFound)
            {
                if (!RefillKeepingTagStart(buf, span, MaxPartial(dimPartial, sdPartial))) { return false; }
                continue;
            }

            if (dimFound && dimPos < sdPos)
            {
                var attrs = span.Slice(dimMatch.AfterName, dimMatch.EndExclusive - dimMatch.AfterName);
                if (CellAttributeParser.TryGetAttributeValue(attrs, RefAttr, out var refBytes))
                {
                    dimension = TryParseRangeBytes(refBytes);
                }

                buf.Advance(dimMatch.EndExclusive);
                continue;
            }

            if (sdMatch.IsEmptyElement)
            {
                emptySheetData = true;
                buf.Advance(sdMatch.EndExclusive);
                return true;
            }
            buf.Advance(sdMatch.EndExclusive);
            return true;
        }
    }

    private static ExcelRange? TryParseRangeBytes(ReadOnlySpan<byte> valueBytes)
    {
        Span<char> charBuf = stackalloc char[Math.Min(64, valueBytes.Length)];
        if (valueBytes.Length > charBuf.Length)
        {
            return Parsing.AddressParser.TryParse(
                System.Text.Encoding.UTF8.GetString(valueBytes).AsSpan(),
                out ExcelRange r) ? r : null;
        }

        int chars = System.Text.Encoding.UTF8.GetChars(valueBytes, charBuf);
        return Parsing.AddressParser.TryParse(charBuf[..chars], out ExcelRange parsed) ? parsed : null;
    }

    internal static bool TryReadRowStart(ScanBuffer buf, ref int rowIndex, out bool isSelfClosing)
    {
        isSelfClosing = false;
        while (true)
        {
            var span = buf.Span;
            int lt = span.IndexOf((byte)'<');
            if (lt < 0)
            {
                buf.Advance(span.Length);
                if (!buf.Refill()) { return false; }
                continue;
            }

            if (lt > 0) { buf.Advance(lt); }
            span = buf.Span;

            // Check for </sheetData> (only when we actually see '</')
            var endResult = TryMatchEndTagAt(span, TagSheetData, out int endLen);
            if (endResult == TagSearchResult.Found) { return false; }
            if (endResult == TagSearchResult.NeedMoreData)
            {
                if (!buf.Refill()) { return false; }
                continue;
            }

            // Check for <row ...>
            var startResult = TryMatchStartTagAt(span, TagRow, out int afterName, out int endExcl, out bool selfClose);
            if (startResult == TagSearchResult.Found)
            {
                var attrBytes = span[afterName..endExcl];
                bool hasR = CellAttributeParser.ParseRowIndex(attrBytes, out int parsedRow);
                if (hasR && parsedRow > ExcelLimits.MaxRows)
                {
                    buf.Advance(endExcl);
                    if (!selfClose) { SkipToEndTag(buf, TagRow); }
                    continue;
                }

                rowIndex = hasR && parsedRow > 0 ? parsedRow : rowIndex + 1;
                isSelfClosing = selfClose;
                buf.Advance(endExcl);
                return true;
            }

            if (startResult == TagSearchResult.NeedMoreData)
            {
                if (!buf.Refill()) { return false; }
                continue;
            }

            // Neither target — skip past this tag's '>'
            SkipPastClosingBracket(buf);
        }
    }

    internal static bool FillRowCells(
        ScanBuffer buf, int rowIndex, SharedStringTable sharedStrings, StyleTable styles,
        bool isDate1904, ReadMode mode, ExcelRange range, ExcelCellValue[] cellBuf,
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

            if (!range.IsUnbounded)
            {
                if (currentCol > range.BottomRight.Column)
                {
                    // Columns are ascending — nothing further in this row can be in range.
                    if (!isEmpty) { SkipToEndTag(buf, TagCell); }
                    SkipToEndTag(buf, TagRow);
                    break;
                }
                if (currentCol < range.TopLeft.Column) { if (!isEmpty) { SkipToEndTag(buf, TagCell); } continue; }
            }

            if (firstCol == 0) { firstCol = currentCol; }

            cellBuf[currentCol - firstCol] = isEmpty
                ? ExcelCellValue.Empty
                : mode == ReadMode.Formulas
                    ? ReadCellValueFormula(buf, kind, styleIdx, sharedStrings, styles, isDate1904)
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
            int lt = span.IndexOf((byte)'<');
            if (lt < 0)
            {
                buf.Advance(span.Length);
                if (!buf.Refill()) { return false; }
                continue;
            }

            if (lt > 0) { buf.Advance(lt); }
            span = buf.Span;

            // Check for </row>
            var endResult = TryMatchEndTagAt(span, TagRow, out int endLen);
            if (endResult == TagSearchResult.Found)
            {
                buf.Advance(endLen);
                return false;
            }

            if (endResult == TagSearchResult.NeedMoreData)
            {
                if (!buf.Refill()) { return false; }
                continue;
            }

            // Check for <c ...>
            var startResult = TryMatchStartTagAt(span, TagCell, out int afterName, out int endExcl, out _);
            if (startResult == TagSearchResult.Found)
            {
                var attrBytes = span[afterName..endExcl];
                CellAttributeParser.ParseCellAttrs(attrBytes, out int parsedCol, out _, out kind, out styleIdx, out isEmpty);
                col = parsedCol > 0 ? parsedCol : col + 1;
                buf.Advance(endExcl);
                return true;
            }

            if (startResult == TagSearchResult.NeedMoreData)
            {
                if (!buf.Refill()) { return false; }
                continue;
            }

            // Neither target — skip past this tag (e.g. <v>, </v>, </c>)
            SkipPastClosingBracket(buf);
        }
    }

    // ── Push-based helpers for ScanSheet<TSink> ─────────────────────────────

    private static ReadOnlySpan<byte> TagMergeCells => "mergeCells"u8;
    private static ReadOnlySpan<byte> TagMergeCell => "mergeCell"u8;
    private static ReadOnlySpan<byte> TagConditionalFormatting => "conditionalFormatting"u8;
    private static ReadOnlySpan<byte> TagDataValidation => "dataValidation"u8;
    private static ReadOnlySpan<byte> TagHyperlink => "hyperlink"u8;

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
        ReadMode mode,
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

            bool inRange = range.IsUnbounded || (currentCol >= range.TopLeft.Column && currentCol <= range.BottomRight.Column);
            if (!inRange)
            {
                if (!isEmpty) { SkipToEndTag(buf, TagCell); }
                if (!range.IsUnbounded && currentCol > range.BottomRight.Column)
                {
                    SkipToEndTag(buf, TagRow);
                    break;
                }
                continue;
            }

            // Formula detection is merged into the value scan — no separate peek needed.
            var value = ReadCellValueForSink(
                buf, currentCol, kind, styleIdx, sharedStrings, styles, isDate1904,
                mode, isEmpty, ref sink, out int rawIndex);

            if (!sink.OnCell(currentCol, kind, styleIdx, value, rawIndex))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Scans bytes remaining after <c>&lt;/sheetData&gt;</c> for merge cells, conditional
    /// formatting, data validations, and hyperlinks, calling the appropriate sink callbacks.
    /// </summary>
    private static void TryScanPostSheetData<TSink>(ScanBuffer buf, ref TSink sink)
        where TSink : struct, IByteSheetSink
    {
        Span<char> refCharBuf = stackalloc char[32];

        while (true)
        {
            var span = buf.Span;

            var mergeStatus = TryFindStartTag(span, TagMergeCell, out var mergeMatch, out int mergePartial);
            var cfStatus = TryFindStartTag(span, TagConditionalFormatting, out var cfMatch, out int cfPartial);
            var dvStatus = TryFindStartTag(span, TagDataValidation, out var dvMatch, out int dvPartial);
            var hyperStatus = TryFindStartTag(span, TagHyperlink, out var hyperMatch, out int hyperPartial);

            int mergePos = mergeStatus == TagSearchResult.Found ? mergeMatch.Start : int.MaxValue;
            int cfPos = cfStatus == TagSearchResult.Found ? cfMatch.Start : int.MaxValue;
            int dvPos = dvStatus == TagSearchResult.Found ? dvMatch.Start : int.MaxValue;
            int hyperPos = hyperStatus == TagSearchResult.Found ? hyperMatch.Start : int.MaxValue;
            int minFoundPos = Math.Min(Math.Min(mergePos, cfPos), Math.Min(dvPos, hyperPos));

            int needMoreStart = NeedMoreMin(mergeStatus, mergeMatch, cfStatus, cfMatch, dvStatus, dvMatch, hyperStatus, hyperMatch);

            if (minFoundPos == int.MaxValue)
            {
                // Nothing found at all
                if (needMoreStart != int.MaxValue) { if (!RefillKeepingTagStart(buf, span, needMoreStart)) { break; } continue; }
                int partial = MaxPartial(MaxPartial(mergePartial, cfPartial), MaxPartial(dvPartial, hyperPartial));
                if (!RefillKeepingTagStart(buf, span, partial)) { break; }
                continue;
            }

            if (needMoreStart < minFoundPos) { if (!RefillKeepingTagStart(buf, span, needMoreStart)) { break; } continue; }

            DispatchPostSheetTag(span, buf, ref sink, refCharBuf, minFoundPos, mergePos, cfPos, dvPos, mergeMatch, cfMatch, dvMatch, hyperMatch);
        }
    }

    private static int NeedMoreMin(
        TagSearchResult s1, StartTagMatch m1,
        TagSearchResult s2, StartTagMatch m2,
        TagSearchResult s3, StartTagMatch m3,
        TagSearchResult s4, StartTagMatch m4)
    {
        int min = int.MaxValue;
        if (s1 == TagSearchResult.NeedMoreData) { min = Math.Min(min, m1.Start); }
        if (s2 == TagSearchResult.NeedMoreData) { min = Math.Min(min, m2.Start); }
        if (s3 == TagSearchResult.NeedMoreData) { min = Math.Min(min, m3.Start); }
        if (s4 == TagSearchResult.NeedMoreData) { min = Math.Min(min, m4.Start); }
        return min;
    }

    private static void DispatchPostSheetTag<TSink>(
        ReadOnlySpan<byte> span, ScanBuffer buf, ref TSink sink, Span<char> refCharBuf,
        int minPos, int mergePos, int cfPos, int dvPos,
        StartTagMatch mergeMatch, StartTagMatch cfMatch, StartTagMatch dvMatch, StartTagMatch hyperMatch)
        where TSink : struct, IByteSheetSink
    {
        if (minPos == mergePos)
        {
            var attrBytes = span.Slice(mergeMatch.AfterName, mergeMatch.EndExclusive - mergeMatch.AfterName);
            if (CellAttributeParser.TryGetRefAttribute(attrBytes, out var refBytes))
            {
                if (refBytes.Length <= refCharBuf.Length)
                {
                    int charLen = Encoding.UTF8.GetChars(refBytes, refCharBuf);
                    if (AddressParser.TryParse(refCharBuf[..charLen], out var mergeRange))
                    {
                        sink.OnMergeCell(new MergedRegion(
                            mergeRange.TopLeft.Row, mergeRange.TopLeft.Column,
                            mergeRange.BottomRight.Row, mergeRange.BottomRight.Column));
                    }
                }
                else
                {
                    var refText = Encoding.UTF8.GetString(refBytes);
                    if (AddressParser.TryParse(refText.AsSpan(), out var mergeRange))
                    {
                        sink.OnMergeCell(new MergedRegion(
                            mergeRange.TopLeft.Row, mergeRange.TopLeft.Column,
                            mergeRange.BottomRight.Row, mergeRange.BottomRight.Column));
                    }
                }
            }
            buf.Advance(mergeMatch.EndExclusive);
        }
        else if (minPos == cfPos)
        {
            sink.OnConditionalFormatting();
            buf.Advance(cfMatch.EndExclusive);
        }
        else if (minPos == dvPos)
        {
            var attributes = span.Slice(dvMatch.AfterName, dvMatch.EndExclusive - dvMatch.AfterName);
            XlsxDataValidationParser.DataValidationBuilder builder =
                XlsxDataValidationParser.ParseAttributes(attributes);
            buf.Advance(dvMatch.EndExclusive);
            ReadOnlySpan<byte> body = dvMatch.IsEmptyElement
                ? []
                : ExtractUntilClose(buf, TagDataValidation);
            sink.OnDataValidation(XlsxDataValidationParser.Complete(builder, body));
        }
        else
        {
            sink.OnHyperlink();
            buf.Advance(hyperMatch.EndExclusive);
        }
    }

}
