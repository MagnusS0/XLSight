using System.Buffers;
using XLSight.Models;
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
    internal static ReadOnlySpan<byte> PatSheetData => "<sheetData"u8;
    internal static ReadOnlySpan<byte> PatSheetDataClose => "</sheetData>"u8;
    internal static ReadOnlySpan<byte> PatRow => "<row"u8;
    internal static ReadOnlySpan<byte> PatRowClose => "</row>"u8;
    internal static ReadOnlySpan<byte> PatC => "<c"u8;
    internal static ReadOnlySpan<byte> PatCClose => "</c>"u8;
    internal static ReadOnlySpan<byte> PatV => "<v>"u8;
    internal static ReadOnlySpan<byte> PatVClose => "</v>"u8;
    internal static ReadOnlySpan<byte> PatTClose => "</t>"u8;

    // ── Entry points ─────────────────────────────────────────────────────────

    /// <summary>
    /// Streams decoded <see cref="ExcelRow"/> values from a worksheet entry stream.
    /// Each yielded row owns its cells array — safe to materialise with .ToList().
    /// Equivalent in output to <c>WorksheetScanner.ScanRows</c> but operates on
    /// raw UTF-8 bytes rather than via <see cref="System.Xml.XmlReader"/>.
    /// </summary>
    internal static IEnumerable<ExcelRow> ScanRows(
        Stream entryStream,
        string[] sharedStrings,
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

        // Single pooled buffer for accumulation — cells are written directly by
        // column index, avoiding List<int> + List<ExcelCellValue> + scatter-copy.
        var cellBuf = ArrayPool<ExcelCellValue>.Shared.Rent(ExcelLimits.MaxColumns);
        int lastRow = 0;

        try
        {
            while (true)
            {
                if (!TryReadRowStart(buf, ref lastRow)) { yield break; }

                int rowIndex = lastRow;
                if (!range.IsUnbounded && rowIndex > range.BottomRight.Row) { yield break; }
                if (!range.IsUnbounded && rowIndex < range.TopLeft.Row) { SkipToTag(buf, PatRowClose); continue; }

                if (FillRowCells(buf, rowIndex, sharedStrings, styles, isDate1904, range, cellBuf,
                    out int startCol, out int width))
                {
                    // Copy the populated slice to an owned array before yielding.
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

    /// <summary>
    /// Opens a zero-allocation cursor over worksheet rows.
    /// Each call to <see cref="SheetCursor.MoveNext"/> overwrites the shared
    /// cell buffer — <see cref="SheetCursor.Current"/> is only valid until the
    /// next <c>MoveNext()</c> call.
    /// </summary>
    internal static SheetCursor OpenCursor(
        Stream entryStream,
        string[] sharedStrings,
        StyleTable styles,
        bool isDate1904,
        ExcelReadMode mode,
        ExcelRange range,
        long seekHint = -1)
        => new(entryStream, sharedStrings, styles, isDate1904, range, seekHint);

    // ── Navigation helpers (internal so SheetCursor can call them) ──────────

    /// <summary>
    /// Advances <paramref name="buf"/> past the opening <c>&lt;sheetData&gt;</c> tag.
    /// If <paramref name="seekHint"/> is non-negative and the stream supports seeking,
    /// skips directly to that offset without scanning.
    /// Returns <see langword="false"/> if the sheet has no data.
    /// </summary>
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
            int idx = span.IndexOf(PatSheetData);
            if (idx < 0) { if (!buf.Refill()) { return false; } continue; }

            int afterName = idx + PatSheetData.Length;
            if (afterName >= span.Length) { buf.Advance(idx); if (!buf.Refill()) { return false; } continue; }

            byte next = span[afterName];
            if (next != (byte)' ' && next != (byte)'>' && next != (byte)'/') { buf.Advance(idx + 1); continue; }
            if (next == (byte)'/') { return false; } // <sheetData/>

            int gt = span[idx..].IndexOf((byte)'>');
            if (gt < 0) { buf.Advance(idx); if (!buf.Refill()) { return false; } continue; }

            // Detect self-closing with whitespace: <sheetData ... />
            if (gt > 0 && span[idx + gt - 1] == (byte)'/') { return false; }

            buf.Advance(idx + gt + 1);
            return true;
        }
    }

    internal static bool TryReadRowStart(ScanBuffer buf, ref int rowIndex)
    {
        while (true)
        {
            var span = buf.Span;
            int closeIdx = span.IndexOf(PatSheetDataClose);
            int rowIdx = span.IndexOf(PatRow);
            if (closeIdx >= 0 && (rowIdx < 0 || closeIdx < rowIdx)) { return false; }
            if (rowIdx < 0) { if (!buf.Refill()) { return false; } continue; }

            int afterName = rowIdx + PatRow.Length;
            if (afterName >= span.Length) { buf.Advance(rowIdx); if (!buf.Refill()) { return false; } continue; }

            byte next = span[afterName];
            if (next != (byte)' ' && next != (byte)'>' && next != (byte)'/') { buf.Advance(rowIdx + 1); continue; }

            int gt = span[rowIdx..].IndexOf((byte)'>');
            if (gt < 0) { buf.Advance(rowIdx); if (!buf.Refill()) { return false; } continue; }

            var attrBytes = span.Slice(rowIdx + PatRow.Length, gt - PatRow.Length + 1);
            bool hasR = CellAttributeParser.ParseRowIndex(attrBytes, out int parsedRow);
            if (hasR && parsedRow > ExcelLimits.MaxRows)
            {
                // Explicit r= is out of Excel range — skip entire row, do not auto-increment.
                buf.Advance(rowIdx + gt + 1);
                SkipToTag(buf, PatRowClose);
                continue;
            }

            if (hasR && parsedRow > 0)
            {
                rowIndex = parsedRow;
            }
            else
            {
                rowIndex++;
            }

            buf.Advance(rowIdx + gt + 1);
            return true;
        }
    }

    /// <summary>
    /// Scans cells in the current row into <paramref name="cellBuf"/>.
    /// Writes decoded values directly at <c>cellBuf[col - startCol]</c>.
    /// Returns <see langword="true"/> if at least one cell was written.
    /// </summary>
    internal static bool FillRowCells(
        ScanBuffer buf, int rowIndex, string[] sharedStrings, StyleTable styles,
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
            if (currentCol > ExcelLimits.MaxColumns) { if (!isEmpty) { SkipToTag(buf, PatCClose); } continue; }

            bool inRange = range.IsUnbounded || range.Contains(new ExcelAddress(currentCol, rowIndex));
            if (!inRange) { if (!isEmpty) { SkipToTag(buf, PatCClose); } continue; }

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

    /// <summary>
    /// Advances <paramref name="buf"/> to the next cell tag within the current row.
    /// Returns <see langword="false"/> when the row end tag is reached or the buffer is exhausted.
    /// On success, <paramref name="col"/> is updated to the 1-based column of the cell found.
    /// </summary>
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
            int closeRow = span.IndexOf(PatRowClose);
            int openC = FindCellTag(span);

            if (closeRow >= 0 && (openC < 0 || closeRow < openC))
            {
                buf.Advance(closeRow + PatRowClose.Length);
                return false;
            }

            if (openC < 0) { if (!buf.Refill()) { return false; } continue; }

            int gt = span[openC..].IndexOf((byte)'>');
            if (gt < 0) { buf.Advance(openC); if (!buf.Refill()) { continue; } continue; }

            var attrBytes = span.Slice(openC + PatC.Length, gt - PatC.Length + 1);
            CellAttributeParser.ParseCellAttrs(attrBytes, out int parsedCol, out _, out kind, out styleIdx, out isEmpty);
            col = parsedCol > 0 ? parsedCol : col + 1;
            buf.Advance(openC + gt + 1);
            return true;
        }
    }

    // ── Cell value readers ──────────────────────────────────────────────────

    internal static ExcelCellValue ReadCellValue(
        ScanBuffer buf, CellDataKind kind, int styleIdx,
        string[] sharedStrings, StyleTable styles, bool isDate1904)
    {
        if (kind == CellDataKind.InlineString) { return ReadInlineString(buf); }

        while (true)
        {
            var span = buf.Span;
            int vIdx = span.IndexOf(PatV);
            int cClose = span.IndexOf(PatCClose);
            if (cClose >= 0 && (vIdx < 0 || cClose < vIdx)) { buf.Advance(cClose + PatCClose.Length); return ExcelCellValue.Empty; }
            if (vIdx < 0) { if (!buf.Refill()) { return ExcelCellValue.Empty; } continue; }
            buf.Advance(vIdx + PatV.Length);
            break;
        }

        var value = ExtractUntilClose(buf, PatVClose);
        SkipToTag(buf, PatCClose);
        return Utf8CellDecoder.Decode(value, kind, styleIdx, sharedStrings, styles, isDate1904);
    }

    internal static ExcelCellValue ReadInlineString(ScanBuffer buf)
    {
        // <is> may contain multiple <r><t>…</t></r> runs (rich text).
        // Concatenate all <t> text nodes; handle <t xml:space="preserve"> and <t/>.
        // seenT tracks whether any <t> element was encountered — an empty <t/> or <t></t>
        // yields FromText("") not Empty, matching XmlReader behaviour.
        bool seenT = false;
        string? first = null;
        System.Text.StringBuilder? sb = null;

        while (true)
        {
            var span = buf.Span;
            int cClose = span.IndexOf(PatCClose);
            int tIdx = FindTTag(span);

            if (cClose >= 0 && (tIdx < 0 || cClose < tIdx))
            {
                buf.Advance(cClose + PatCClose.Length);
                break;
            }

            if (tIdx < 0) { if (!buf.Refill()) { break; } continue; }

            // Advance past the full opening <t...> tag.
            int relGt = span[tIdx..].IndexOf((byte)'>');
            if (relGt < 0) { buf.Advance(tIdx); if (!buf.Refill()) { break; } continue; }

            seenT = true;
            bool selfClosing = relGt > 0 && span[tIdx + relGt - 1] == (byte)'/';
            buf.Advance(tIdx + relGt + 1);
            if (selfClosing) { continue; }

            var textBytes = ExtractUntilClose(buf, PatTClose);
            if (textBytes.IsEmpty) { continue; }

            // Decode (handles XML entity unescaping) before next buffer operation.
            var cell = Utf8CellDecoder.Decode(textBytes, CellDataKind.InlineString, 0, [], StyleTable.Default, false);
            if (cell.CellType == ExcelCellType.Text)
            {
                var text = cell.AsText();
                if (first is null) { first = text; }
                else { (sb ??= new System.Text.StringBuilder(first)).Append(text); }
            }
        }

        var result = sb?.ToString() ?? first;
        if (result is not null) { return ExcelCellValue.FromText(result); }
        return seenT ? ExcelCellValue.FromText(string.Empty) : ExcelCellValue.Empty;
    }

    // Finds <t with valid tag-boundary byte ('>', ' ', or '/') after 't'.
    // Returns -1 if not found. Rejects false matches like <text> or </t>.
    private static int FindTTag(ReadOnlySpan<byte> span)
    {
        ReadOnlySpan<byte> pat = "<t"u8;
        int search = 0;
        while (true)
        {
            int idx = span[search..].IndexOf(pat);
            if (idx < 0) { return -1; }
            idx += search;
            int afterName = idx + 2;
            if (afterName >= span.Length) { return -1; }
            byte next = span[afterName];
            if (next == (byte)'>' || next == (byte)' ' || next == (byte)'/') { return idx; }
            search = idx + 1;
        }
    }

    // ── Buffer helpers ──────────────────────────────────────────────────────

    /// <summary>
    /// Finds &lt;c with a valid tag-boundary byte (space, '&gt;', or '/') after the name.
    /// Returns the position in <paramref name="span"/> or -1.
    /// </summary>
    internal static int FindCellTag(ReadOnlySpan<byte> span)
    {
        int search = 0;
        while (true)
        {
            int idx = span[search..].IndexOf(PatC);
            if (idx < 0) { return -1; }

            idx += search;
            int afterName = idx + PatC.Length;
            if (afterName >= span.Length) { return -1; }

            byte next = span[afterName];
            if (next == (byte)' ' || next == (byte)'>' || next == (byte)'/') { return idx; }

            search = idx + 1;
        }
    }

    /// <summary>
    /// Extracts bytes up to (not including) <paramref name="closeTag"/>, advances past it,
    /// and returns the extracted span (valid until the next <see cref="ScanBuffer"/> operation).
    /// </summary>
    internal static ReadOnlySpan<byte> ExtractUntilClose(ScanBuffer buf, ReadOnlySpan<byte> closeTag)
    {
        while (true)
        {
            var span = buf.Span;
            int closeIdx = span.IndexOf(closeTag);
            if (closeIdx >= 0)
            {
                var value = span[..closeIdx];
                buf.Advance(closeIdx + closeTag.Length);
                return value;
            }

            if (!buf.Refill()) { return ReadOnlySpan<byte>.Empty; }
        }
    }

    internal static void SkipToTag(ScanBuffer buf, ReadOnlySpan<byte> tag)
    {
        while (true)
        {
            int idx = buf.Span.IndexOf(tag);
            if (idx >= 0) { buf.Advance(idx + tag.Length); return; }
            if (!buf.Refill()) { return; }
        }
    }
}
