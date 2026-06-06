using System.Buffers.Text;
using System.Net;
using System.Text;
using XLSight.Internal.Analysis;
using XLSight.Internal.Metadata;
using XLSight.Internal.Sinks;
using static XLSight.Internal.Readers.Xlsx.XmlByteReader;

namespace XLSight.Internal.Readers.Xlsx;

internal static partial class XlsxSheetScanner
{
    // ── Cell value readers ────────────────────────────────────────────────────

    internal static ExcelCellValue ReadCellValue(
        ScanBuffer buf, CellDataKind kind, int styleIdx,
        SharedStringTable sharedStrings, StyleTable styles, bool isDate1904)
    {
        if (kind == CellDataKind.InlineString) { return ReadInlineString(buf); }
        return ReadCellValueCore(
            buf, kind, styleIdx, sharedStrings, styles, isDate1904,
            decodeSharedString: true, out _);
    }

    /// <summary>
    /// Variant of <see cref="ReadCellValue"/> for <see cref="ReadMode.Formulas"/>.
    /// Scans for <c>&lt;f&gt;</c> and <c>&lt;v&gt;</c> simultaneously; when <c>&lt;f&gt;</c>
    /// appears first the raw formula text is returned as <see cref="ExcelCellValue.FromFormula"/>.
    /// Falls back to the normal cached-value decode when no formula tag is present.
    /// </summary>
    private static ExcelCellValue ReadCellValueFormula(
        ScanBuffer buf, CellDataKind kind, int styleIdx,
        SharedStringTable sharedStrings, StyleTable styles, bool isDate1904)
    {
        if (kind == CellDataKind.InlineString) { return ReadInlineString(buf); }

        while (true)
        {
            var span = buf.Span;
            var cStatus = TryFindEndTag(span, TagCell, out int cIdx, out int cLen, out int cPartial);
            int limit = cStatus == TagSearchResult.Found ? cIdx : span.Length;
            var searchSpan = span[..limit];

            var fStatus = TryFindStartTag(searchSpan, TagFormula, out var fMatch, out int fPartial);
            var vStatus = TryFindStartTag(searchSpan, TagValue, out var vMatch, out int vPartial);

            if (fStatus == TagSearchResult.NeedMoreData || vStatus == TagSearchResult.NeedMoreData)
            {
                if (cStatus == TagSearchResult.Found) { buf.Advance(cIdx + cLen); return ExcelCellValue.Empty; }
                if (!RefillKeepingTagStart(buf, span, MaxPartial(MaxPartial(fPartial, vPartial), cPartial))) { return ExcelCellValue.Empty; }
                continue;
            }

            bool fFound = fStatus == TagSearchResult.Found;
            bool vFound = vStatus == TagSearchResult.Found;

            if (!fFound && !vFound)
            {
                if (cStatus == TagSearchResult.Found) { buf.Advance(cIdx + cLen); return ExcelCellValue.Empty; }
                if (!RefillKeepingTagStart(buf, span, MaxPartial(MaxPartial(fPartial, vPartial), cPartial))) { return ExcelCellValue.Empty; }
                continue;
            }

            if (fFound && (!vFound || fMatch.Start < vMatch.Start))
            {
                buf.Advance(fMatch.EndExclusive);
                if (!fMatch.IsEmptyElement) { return ExtractFormulaValue(buf); }
                continue; // <f/> with no text — look for <v>
            }

            if (vFound) { return ExtractCachedValue(buf, vMatch, kind, styleIdx, sharedStrings, styles, isDate1904); }

            if (!RefillKeepingTagStart(buf, span, MaxPartial(MaxPartial(fPartial, vPartial), cPartial))) { return ExcelCellValue.Empty; }
        }
    }

    private static ExcelCellValue ExtractFormulaValue(ScanBuffer buf)
    {
        var fb = ExtractUntilClose(buf, TagFormula);
        var text = Encoding.UTF8.GetString(fb);
        SkipToEndTag(buf, TagCell);
        if (text.Contains('&', StringComparison.Ordinal)) { text = WebUtility.HtmlDecode(text); }
        return ExcelCellValue.FromFormula(text);
    }

    private static ExcelCellValue ExtractCachedValue(
        ScanBuffer buf, StartTagMatch vMatch, CellDataKind kind, int styleIdx,
        SharedStringTable sharedStrings, StyleTable styles, bool isDate1904)
    {
        buf.Advance(vMatch.EndExclusive);
        if (vMatch.IsEmptyElement) { SkipToEndTag(buf, TagCell); return ExcelCellValue.Empty; }
        var vb = ExtractUntilClose(buf, TagValue);
        var decoded = Utf8CellDecoder.Decode(vb, kind, styleIdx, sharedStrings, styles, isDate1904);
        SkipToEndTag(buf, TagCell);
        return decoded;
    }

    /// <summary>
    /// Variant of <see cref="ReadCellValue"/> for <see cref="CellDataKind.SharedString"/> cells
    /// that also outputs the raw SST index. When <paramref name="decode"/> is <see langword="false"/>
    /// the string is not materialised and <see cref="ExcelCellValue.Empty"/> is returned — the caller
    /// must use <paramref name="sstIndex"/> instead.
    /// </summary>
    private static ExcelCellValue ReadCellValueWithSstIndex(
        ScanBuffer buf, int styleIdx,
        SharedStringTable sharedStrings, StyleTable styles, bool isDate1904,
        bool decode,
        out int sstIndex)
        => ReadCellValueCore(
            buf, CellDataKind.SharedString, styleIdx, sharedStrings, styles, isDate1904,
            decode, out sstIndex);

    private static ExcelCellValue ReadCellValueCore(
        ScanBuffer buf, CellDataKind kind, int styleIdx,
        SharedStringTable sharedStrings, StyleTable styles, bool isDate1904,
        bool decodeSharedString,
        out int sstIndex)
    {
        sstIndex = -1;
        while (true)
        {
            var span = buf.Span;
            var closeStatus = TryFindEndTag(span, TagCell, out int cClose, out int cCloseLen, out int closePartial);

            // Constrain <v> search to the current cell body.
            int limit = closeStatus == TagSearchResult.Found ? cClose : span.Length;
            var valueStatus = TryFindStartTag(span[..limit], TagValue, out var valueMatch, out int valuePartial);

            if (valueStatus == TagSearchResult.NotFound)
            {
                if (closeStatus == TagSearchResult.Found) { buf.Advance(cClose + cCloseLen); return ExcelCellValue.Empty; }
                if (!RefillKeepingTagStart(buf, span, MaxPartial(valuePartial, closePartial))) { return ExcelCellValue.Empty; }
                continue;
            }

            if (valueStatus == TagSearchResult.NeedMoreData)
            {
                if (closeStatus == TagSearchResult.Found) { buf.Advance(cClose + cCloseLen); return ExcelCellValue.Empty; }
                if (!RefillKeepingTagStart(buf, span, valueMatch.Start)) { return ExcelCellValue.Empty; }
                continue;
            }

            // Fast path: </c> and </v> both in span — decode inline without intermediate advance.
            if (!valueMatch.IsEmptyElement && closeStatus == TagSearchResult.Found)
            {
                var body = span[valueMatch.EndExclusive..cClose];
                if (TryFindEndTag(body, TagValue, out int vClose, out _, out _) == TagSearchResult.Found)
                {
                    var decoded = DecodeCellValueBytes(
                        body[..vClose], kind, styleIdx, sharedStrings, styles, isDate1904,
                        decodeSharedString, out sstIndex);
                    buf.Advance(cClose + cCloseLen);
                    return decoded;
                }
            }

            buf.Advance(valueMatch.EndExclusive);
            if (valueMatch.IsEmptyElement) { SkipToEndTag(buf, TagCell); return ExcelCellValue.Empty; }

            break;
        }

        var valueBytes = ExtractUntilClose(buf, TagValue);
        var result = DecodeCellValueBytes(
            valueBytes, kind, styleIdx, sharedStrings, styles, isDate1904,
            decodeSharedString, out sstIndex);
        SkipToEndTag(buf, TagCell);
        return result;
    }

    private static ExcelCellValue DecodeCellValueBytes(
        ReadOnlySpan<byte> valueBytes, CellDataKind kind, int styleIdx,
        SharedStringTable sharedStrings, StyleTable styles, bool isDate1904,
        bool decodeSharedString, out int sstIndex)
    {
        if (kind == CellDataKind.SharedString)
        {
            return DecodeSharedStringValue(valueBytes, styleIdx, sharedStrings, styles, isDate1904, decodeSharedString, out sstIndex);
        }

        sstIndex = -1;
        return Utf8CellDecoder.Decode(valueBytes, kind, styleIdx, sharedStrings, styles, isDate1904);
    }

    private static ExcelCellValue DecodeSharedStringValue(
        ReadOnlySpan<byte> valueBytes, int styleIdx,
        SharedStringTable sharedStrings, StyleTable styles, bool isDate1904,
        bool decode, out int sstIndex)
    {
        if (!Utf8Parser.TryParse(valueBytes, out sstIndex, out _)) { sstIndex = -1; }
        return decode
            ? Utf8CellDecoder.Decode(valueBytes, CellDataKind.SharedString, styleIdx, sharedStrings, styles, isDate1904)
            : ExcelCellValue.Empty;
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
                if (!RefillKeepingTagStart(buf, span, textMatch.Start)) { break; }
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

    private static void AccumulateInlineText(
        ReadOnlySpan<byte> bytes, ref string? first, ref StringBuilder? sb)
    {
        var cell = Utf8CellDecoder.Decode(bytes, CellDataKind.InlineString, 0, SharedStringTable.Empty, StyleTable.Default, false);
        if (cell.CellType != CellType.Text) { return; }
        var text = cell.AsText();
        if (first is null) { first = text; }
        else { (sb ??= new StringBuilder(first)).Append(text); }
    }

    // ── Sink dispatch ─────────────────────────────────────────────────────────

    // Reads the cell value using the correct path based on mode and kind.
    // When sink.TracksFormulas, formula detection is merged into the value scan (single pass).
    // The JIT dead-code-eliminates the formula-tracking branch for sinks where TracksFormulas = false.
    private static ExcelCellValue ReadCellValueForSink<TSink>(
        ScanBuffer buf, int column, CellDataKind kind, int styleIdx,
        SharedStringTable sharedStrings, StyleTable styles, bool isDate1904,
        ReadMode mode, bool isEmpty, ref TSink sink, out int rawIndex)
        where TSink : struct, IByteSheetSink
    {
        rawIndex = -1;
        if (isEmpty) { return ExcelCellValue.Empty; }
        if (mode == ReadMode.Formulas) { return ReadCellValueFormula(buf, kind, styleIdx, sharedStrings, styles, isDate1904); }

        if ((sink.TracksFormulas || sink.TracksFormulaReferences) && kind != CellDataKind.InlineString)
        {
            return ReadCellValueWithFormulaDetect(buf, kind, styleIdx, sharedStrings, styles, isDate1904,
                sink.NeedsDecodedValue, column, ref sink, out rawIndex);
        }

        if (kind == CellDataKind.SharedString)
        {
            return ReadCellValueWithSstIndex(buf, styleIdx, sharedStrings, styles, isDate1904,
                decode: sink.NeedsDecodedValue, out rawIndex);
        }

        return ReadCellValue(buf, kind, styleIdx, sharedStrings, styles, isDate1904);
    }

    /// <summary>
    /// Scans a cell body for <c>&lt;f&gt;</c>, <c>&lt;v&gt;</c>, and <c>&lt;/c&gt;</c> in a single
    /// pass, returning the cached value from <c>&lt;v&gt;</c> and emitting formula callbacks.
    /// Finds <c>&lt;/c&gt;</c> first to bound all inner searches to O(CellSize) rather than O(BufferSize).
    /// </summary>
    private static ExcelCellValue ReadCellValueWithFormulaDetect<TSink>(
        ScanBuffer buf, CellDataKind kind, int styleIdx,
        SharedStringTable sharedStrings, StyleTable styles, bool isDate1904,
        bool decode, int column, ref TSink sink, out int sstIndex)
        where TSink : struct, IByteSheetSink
    {
        sstIndex = -1;
        bool formulaFound = false;

        while (true)
        {
            var span = buf.Span;
            var cStatus = TryFindEndTag(span, TagCell, out int cIdx, out int cLen, out int cPartial);
            int limit = cStatus == TagSearchResult.Found ? cIdx : span.Length;
            var searchSpan = span[..limit];

            var fStatus = TryFindStartTag(searchSpan, TagFormula, out var fMatch, out int fPartial);
            var vStatus = TryFindStartTag(searchSpan, TagValue, out var vMatch, out int vPartial);

            bool fFound = fStatus == TagSearchResult.Found;
            bool vFound = vStatus == TagSearchResult.Found;

            // Both branches require the same refill-or-bail logic: consolidate them.
            bool needRefill = fStatus == TagSearchResult.NeedMoreData
                || vStatus == TagSearchResult.NeedMoreData
                || (!fFound && !vFound);
            if (needRefill)
            {
                if (cStatus == TagSearchResult.Found) { buf.Advance(cIdx + cLen); return ExcelCellValue.Empty; }
                if (!RefillKeepingTagStart(buf, span, MaxPartial(MaxPartial(fPartial, vPartial), cPartial))) { return ExcelCellValue.Empty; }
                continue;
            }

            if (fFound && (!vFound || fMatch.Start < vMatch.Start))
            {
                AdvancePastFormula(buf, span, fMatch, column, ref formulaFound, ref sink);
                continue;
            }

            // vFound is true here.
            if (vMatch.IsEmptyElement)
            {
                buf.Advance(cStatus == TagSearchResult.Found ? cIdx + cLen : vMatch.EndExclusive);
                if (cStatus != TagSearchResult.Found) { SkipToEndTag(buf, TagCell); }
                return ExcelCellValue.Empty;
            }

            // Fast path: </c> and </v> both in span — decode inline, single advance.
            if (cStatus == TagSearchResult.Found)
            {
                var body = span[vMatch.EndExclusive..cIdx];
                if (TryFindEndTag(body, TagValue, out int vClose, out _, out _) == TagSearchResult.Found)
                {
                    ExcelCellValue fast = kind == CellDataKind.SharedString
                        ? DecodeSharedStringValue(body[..vClose], styleIdx, sharedStrings, styles, isDate1904, decode, out sstIndex)
                        : Utf8CellDecoder.Decode(body[..vClose], kind, styleIdx, sharedStrings, styles, isDate1904);
                    buf.Advance(cIdx + cLen);
                    return fast;
                }
            }

            return ExtractBoundedCachedValue(buf, vMatch, kind, styleIdx, sharedStrings, styles, isDate1904, decode, out sstIndex);
        }
    }

    private static ExcelCellValue ExtractBoundedCachedValue(
        ScanBuffer buf, StartTagMatch vMatch, CellDataKind kind, int styleIdx,
        SharedStringTable sharedStrings, StyleTable styles, bool isDate1904,
        bool decode, out int sstIndex)
    {
        sstIndex = -1;
        buf.Advance(vMatch.EndExclusive);
        if (vMatch.IsEmptyElement) { SkipToEndTag(buf, TagCell); return ExcelCellValue.Empty; }
        var vBytes = ExtractUntilClose(buf, TagValue);
        ExcelCellValue result = kind == CellDataKind.SharedString
            ? DecodeSharedStringValue(vBytes, styleIdx, sharedStrings, styles, isDate1904, decode, out sstIndex)
            : Utf8CellDecoder.Decode(vBytes, kind, styleIdx, sharedStrings, styles, isDate1904);
        SkipToEndTag(buf, TagCell);
        return result;
    }

    // Records formula presence and advances past the <f>...</f> body.
    private static void AdvancePastFormula<TSink>(
        ScanBuffer buf,
        ReadOnlySpan<byte> span,
        StartTagMatch fMatch,
        int column,
        ref bool formulaFound,
        ref TSink sink)
        where TSink : struct, IByteSheetSink
    {
        int sharedIndex = -1;
        bool isShared = false;
        if (!formulaFound)
        {
            var fAttrs = span.Slice(fMatch.AfterName, fMatch.EndExclusive - fMatch.AfterName);
            bool hasType = CellAttributeParser.TryGetAttributeValue(fAttrs, TAttr, out var typeBytes);
            bool isArrayFormula = hasType && typeBytes.SequenceEqual("array"u8);
            isShared = hasType && typeBytes.SequenceEqual("shared"u8);
            if (isShared && CellAttributeParser.TryGetAttributeValue(fAttrs, "si="u8, out var sharedIndexBytes))
            {
                _ = System.Buffers.Text.Utf8Parser.TryParse(sharedIndexBytes, out sharedIndex, out _);
            }

            if (sink.TracksFormulas)
            {
                sink.OnFormula(column, isArrayFormula);
            }
            formulaFound = true;
        }

        buf.Advance(fMatch.EndExclusive);
        if (fMatch.IsEmptyElement)
        {
            if (isShared && sharedIndex >= 0)
            {
                sink.OnSharedFormulaReference(sharedIndex);
            }

            return;
        }

        ReadOnlySpan<byte> formula = ExtractUntilClose(buf, TagFormula);
        if (isShared && sharedIndex >= 0)
        {
            sink.OnSharedFormulaDefinition(sharedIndex);
        }

        if (sink.TracksFormulaReferences && !formula.IsEmpty)
        {
            FormulaReferenceParser.ParseUtf8(formula, ref sink);
        }
    }
}
