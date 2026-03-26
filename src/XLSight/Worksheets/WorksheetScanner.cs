using System.Buffers;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text;
using System.Xml;

using XLSight.Models;
using XLSight.Models.Analysis;
using XLSight.Parsing;
using XLSight.Styles;

namespace XLSight.Worksheets;

internal static class WorksheetScanner
{
    internal static void Scan<TSink>(Stream entryStream, XlsxNameTable names, ref TSink sink)
        where TSink : struct, IWorksheetSink
    {
        var settings = XlsxReaderSettings.Create(names.Table);
        using var reader = XmlReader.Create(entryStream, settings);

        char[] valueBuf = ArrayPool<char>.Shared.Rent(256);
        try
        {
            int lastRowSeen = 0;
            while (reader.Read())
            {
                if (reader.NodeType != XmlNodeType.Element)
                {
                    continue;
                }

                if (ReferenceEquals(reader.LocalName, names.Dimension))
                {
                    var dimRef = reader.GetAttribute(names.Ref);
                    if (dimRef is not null && AddressParser.TryParse(dimRef, out var dim))
                    {
                        sink.OnDimension(in dim);
                    }
                }
                else if (ReferenceEquals(reader.LocalName, names.Row))
                {
                    var rowStr = reader.GetAttribute(names.R);
                    lastRowSeen = rowStr is not null && int.TryParse(rowStr, NumberStyles.None, CultureInfo.InvariantCulture, out int parsedRow)
                        ? parsedRow : lastRowSeen + 1;
                    sink.OnRowStart(lastRowSeen);
                }
                else if (ReferenceEquals(reader.LocalName, names.C))
                {
                    var cell = ParseCell(reader, names, valueBuf, readFormulas: true);
                    if (!sink.OnCell(in cell))
                    {
                        return;
                    }
                }
                else if (ReferenceEquals(reader.LocalName, names.MergeCell))
                {
                    var refStr = reader.GetAttribute(names.Ref);
                    if (refStr is not null && AddressParser.TryParse(refStr, out var mergeRange))
                    {
                        sink.OnMergeCell(new ExcelMergedRegion(
                            mergeRange.TopLeft.Row, mergeRange.TopLeft.Column,
                            mergeRange.BottomRight.Row, mergeRange.BottomRight.Column));
                    }
                }
                else if (!IsTransparentElement(reader.LocalName, names))
                {
                    reader.Skip();
                }
            }
            sink.OnEnd();
        }
        finally
        {
            ArrayPool<char>.Shared.Return(valueBuf, clearArray: false);
        }
    }

    /// <summary>
    /// Non-generic overload for use with class-based <see cref="IWorksheetSink"/> implementations.
    /// Prefer the generic <c>Scan&lt;TSink&gt;</c> overload for struct sinks to avoid virtual dispatch.
    /// </summary>
    internal static void Scan(Stream entryStream, XlsxNameTable names, IWorksheetSink sink)
    {
        var settings = XlsxReaderSettings.Create(names.Table);
        using var reader = XmlReader.Create(entryStream, settings);

        char[] valueBuf = ArrayPool<char>.Shared.Rent(256);
        try
        {
            while (reader.Read())
            {
                if (reader.NodeType != XmlNodeType.Element)
                {
                    continue;
                }

                if (ReferenceEquals(reader.LocalName, names.Dimension))
                {
                    var dimRef = reader.GetAttribute(names.Ref);
                    if (dimRef is not null && AddressParser.TryParse(dimRef, out var dim))
                    {
                        sink.OnDimension(in dim);
                    }
                }
                else if (ReferenceEquals(reader.LocalName, names.Row))
                {
                    var rowStr = reader.GetAttribute(names.R);
                    if (rowStr is not null && int.TryParse(rowStr, NumberStyles.None, CultureInfo.InvariantCulture, out int row))
                    {
                        sink.OnRowStart(row);
                    }
                }
                else if (ReferenceEquals(reader.LocalName, names.C))
                {
                    var cell = ParseCell(reader, names, valueBuf, readFormulas: true);
                    if (!sink.OnCell(in cell))
                    {
                        return;
                    }
                }
                else if (ReferenceEquals(reader.LocalName, names.MergeCell))
                {
                    var refStr = reader.GetAttribute(names.Ref);
                    if (refStr is not null && AddressParser.TryParse(refStr, out var mergeRange))
                    {
                        sink.OnMergeCell(new ExcelMergedRegion(
                            mergeRange.TopLeft.Row, mergeRange.TopLeft.Column,
                            mergeRange.BottomRight.Row, mergeRange.BottomRight.Column));
                    }
                }
                else if (!IsTransparentElement(reader.LocalName, names))
                {
                    reader.Skip();
                }
            }
            sink.OnEnd();
        }
        finally
        {
            ArrayPool<char>.Shared.Return(valueBuf, clearArray: false);
        }
    }

    internal static async Task ScanAsync(
        Stream entryStream, XlsxNameTable names, IWorksheetSink sink, CancellationToken ct)
    {
        var settings = XlsxReaderSettings.Create(names.Table);
        settings.Async = true;
        using var reader = XmlReader.Create(entryStream, settings);

        char[] valueBuf = ArrayPool<char>.Shared.Rent(256);
        try
        {
            int lastRowSeen = 0;
            while (await reader.ReadAsync().ConfigureAwait(false))
            {
                ct.ThrowIfCancellationRequested();
                if (reader.NodeType != XmlNodeType.Element) { continue; }
                if (ReferenceEquals(reader.LocalName, names.Dimension))
                {
                    var dimRef = reader.GetAttribute(names.Ref);
                    if (dimRef is not null && AddressParser.TryParse(dimRef, out var dim))
                    {
                        sink.OnDimension(in dim);
                    }
                }
                else if (ReferenceEquals(reader.LocalName, names.Row))
                {
                    var rowStr = reader.GetAttribute(names.R);
                    lastRowSeen = rowStr is not null && int.TryParse(rowStr, NumberStyles.None, CultureInfo.InvariantCulture, out int parsedRow)
                        ? parsedRow : lastRowSeen + 1;
                    sink.OnRowStart(lastRowSeen);
                }
                else if (ReferenceEquals(reader.LocalName, names.C))
                {
                    var cell = ParseCell(reader, names, valueBuf, readFormulas: true);
                    if (!sink.OnCell(in cell))
                    {
                        return;
                    }
                }
                else if (ReferenceEquals(reader.LocalName, names.MergeCell))
                {
                    var refStr = reader.GetAttribute(names.Ref);
                    if (refStr is not null && AddressParser.TryParse(refStr, out var mergeRange))
                    {
                        sink.OnMergeCell(new ExcelMergedRegion(
                            mergeRange.TopLeft.Row, mergeRange.TopLeft.Column,
                            mergeRange.BottomRight.Row, mergeRange.BottomRight.Column));
                    }
                }
                else if (!IsTransparentElement(reader.LocalName, names))
                {
                    reader.Skip();
                }
            }
            sink.OnEnd();
        }
        finally
        {
            ArrayPool<char>.Shared.Return(valueBuf, clearArray: false);
        }
    }

    /// <summary>
    /// Yields decoded <see cref="ExcelRow"/> instances inline on the caller's thread.
    /// No background thread or Channel — optimal for both full-file and Take(N) streaming.
    /// Uses nested ReadToDescendant/ReadToNextSibling loops to skip directly between
    /// sheetData → row → c elements without visiting intermediate nodes, then span-based
    /// decode to avoid intermediate string allocation for numeric/SST-index/bool cells.
    /// </summary>
    internal static IEnumerable<ExcelRow> ScanRows(
        Stream entryStream,
        XlsxNameTable names,
        string[] sharedStrings,
        StyleTable styles,
        bool isDate1904,
        ExcelReadMode mode,
        ExcelRange range)
    {
        var settings = XlsxReaderSettings.Create(names.Table);
        using var reader = XmlReader.Create(entryStream, settings);
        var valueBuf = new char[256];
        var rowCols = new List<int>(16);
        var rowVals = new List<ExcelCellValue>(16);
        int lastRowSeen = 0;
        bool readFormulas = mode == ExcelReadMode.Formulas;
        if (!ReadToSheetData(reader, names)) { yield break; }
        while (ReadToNextRow(reader, names))
        {
            var rowStr = reader.GetAttribute(names.R);
            lastRowSeen = rowStr is not null && int.TryParse(rowStr, NumberStyles.None, CultureInfo.InvariantCulture, out int parsedRow)
                ? parsedRow : lastRowSeen + 1;
            if (!range.IsUnbounded && lastRowSeen > range.BottomRight.Row) { yield break; }
            bool skipRow = lastRowSeen > ExcelLimits.MaxRows
                || (!range.IsUnbounded && lastRowSeen < range.TopLeft.Row);
            if (skipRow)
            {
                reader.Skip();
                continue;
            }

            if (!reader.IsEmptyElement)
            {
                int rowDepth = reader.Depth;
                int lastCellCol = 0;
                while (reader.Read())
                {
                    if (reader.NodeType == XmlNodeType.EndElement
                        && reader.Depth == rowDepth
                        && ReferenceEquals(reader.LocalName, names.Row))
                    {
                        break;
                    }

                    if (reader.NodeType != XmlNodeType.Element
                        || !ReferenceEquals(reader.LocalName, names.C))
                    {
                        continue;
                    }

                    TryDecodeCellForRow(reader, names, valueBuf, sharedStrings, styles, isDate1904, mode, readFormulas,
                        out int col, out ExcelCellValue val);
                    if (col == 0) { col = lastCellCol + 1; } // positional fallback per OOXML §18.3.1.4
                    lastCellCol = col;
                    if (range.IsUnbounded || range.Contains(new ExcelAddress(col, lastRowSeen)))
                    {
                        rowCols.Add(col);
                        rowVals.Add(val);
                    }
                }
            }
            if (rowCols.Count > 0)
            {
                yield return BuildExcelRow(lastRowSeen, rowCols, rowVals);
                rowCols.Clear();
                rowVals.Clear();
            }
        }
    }

    private static bool ReadToSheetData(XmlReader reader, XlsxNameTable names)
    {
        while (reader.Read())
        {
            if (reader.NodeType == XmlNodeType.Element
                && ReferenceEquals(reader.LocalName, names.SheetData))
            {
                return true;
            }
        }

        return false;
    }

    private static bool ReadToNextRow(XmlReader reader, XlsxNameTable names)
    {
        while (reader.Read())
        {
            if (reader.NodeType == XmlNodeType.EndElement
                && ReferenceEquals(reader.LocalName, names.SheetData))
            {
                return false;
            }

            if (reader.NodeType == XmlNodeType.Element
                && ReferenceEquals(reader.LocalName, names.Row))
            {
                return true;
            }
        }

        return false;
    }


    internal static ExcelRow BuildExcelRow(int rowIndex, Dictionary<int, ExcelCellValue> cells)
    {
        int minCol = int.MaxValue;
        int maxCol = int.MinValue;

        foreach (var col in cells.Keys)
        {
            if (col < minCol)
            {
                minCol = col;
            }

            if (col > maxCol)
            {
                maxCol = col;
            }
        }

        int width = maxCol - minCol + 1;
        var buffer = new ExcelCellValue[width];
        foreach (var (col, value) in cells)
        {
            buffer[col - minCol] = value;
        }

        return new ExcelRow(rowIndex, buffer, minCol);
    }

    // Streaming path overload: cells already in column order, no hashing needed.
    private static ExcelRow BuildExcelRow(int rowIndex, List<int> cols, List<ExcelCellValue> vals)
    {
        int minCol = cols[0];
        int maxCol = cols[^1];
        int width = maxCol - minCol + 1;
        var buffer = new ExcelCellValue[width];
        for (int i = 0; i < cols.Count; i++)
        {
            buffer[cols[i] - minCol] = vals[i];
        }

        return new ExcelRow(rowIndex, buffer, minCol);
    }

    // Fused parse+decode for the ScanRows streaming path.
    // Reads cell attributes and <v>/<f>/<is> children directly into valueBuf,
    // then decodes to ExcelCellValue from the span — no intermediate string for numeric/bool/SST-index.
    private static void ParseAndDecodeCell(
        XmlReader reader,
        XlsxNameTable names,
        char[] valueBuf,
        string[] sharedStrings,
        StyleTable styles,
        bool isDate1904,
        ExcelReadMode mode,
        bool readFormulas,
        out int column,
        out ExcelCellValue value)
    {
        ReadCellAttributes(reader, names, out string? cellRef, out string? styleStr, out string? typeStr);

        int row = 0, col = 0;
        if (cellRef is not null && CellReferenceParser.TryParse(cellRef, out var addr))
        {
            row  = addr.Row;
            col  = addr.Column;
        }
        column = col;

        int styleIndex = 0;
        if (styleStr is not null)
        {
            int.TryParse(styleStr, NumberStyles.None, CultureInfo.InvariantCulture, out styleIndex);
        }

        CellDataKind kind = typeStr switch
        {
            "s"         => CellDataKind.SharedString,
            "b"         => CellDataKind.Boolean,
            "inlineStr" => CellDataKind.InlineString,
            "str"       => CellDataKind.FormulaString,
            "e"         => CellDataKind.Error,
            _           => CellDataKind.Number,
        };

        if (reader.IsEmptyElement)
        {
            value = ExcelCellValue.Empty;
            return;
        }

        ReadCellChildren(reader, names, valueBuf, readFormulas,
            out int valueLen, out string? valueOverflow,
            out string? inlineString, out string? formulaText);

        value = DecodeFromSpan(
            valueBuf.AsSpan(0, valueLen), valueOverflow,
            kind, styleIndex, inlineString, formulaText,
            sharedStrings, styles, isDate1904, mode);
    }

    // Fused parse+decode for the ScanRows nested-loop path.
    // Postcondition: reader is on </c> EndElement, or on <c/> if cell was an empty element.
    private static void TryDecodeCellForRow(
        XmlReader reader,
        XlsxNameTable names,
        char[] valueBuf,
        string[] sharedStrings,
        StyleTable styles,
        bool isDate1904,
        ExcelReadMode mode,
        bool readFormulas,
        out int column,
        out ExcelCellValue value)
    {
        ReadCellAttributes(reader, names, out string? cellRef, out string? styleStr, out string? typeStr);

        int col = 0;
        if (cellRef is not null && CellReferenceParser.TryParse(cellRef, out var addr))
        {
            col = addr.Column;
        }
        column = col;

        int styleIndex = 0;
        if (styleStr is not null)
        {
            int.TryParse(styleStr, NumberStyles.None, CultureInfo.InvariantCulture, out styleIndex);
        }

        CellDataKind kind = typeStr switch
        {
            "s"         => CellDataKind.SharedString,
            "b"         => CellDataKind.Boolean,
            "inlineStr" => CellDataKind.InlineString,
            "str"       => CellDataKind.FormulaString,
            "e"         => CellDataKind.Error,
            _           => CellDataKind.Number,
        };

        if (reader.IsEmptyElement)
        {
            value = ExcelCellValue.Empty;
            return; // reader stays on <c/>; ReadToNextSibling handles it
        }

        value = ReadCellValueNested(reader, names, valueBuf, kind, styleIndex,
            readFormulas, sharedStrings, styles, isDate1904, mode);
    }

    // Reads and decodes the value of a non-empty <c> element using nested navigation.
    // For formula mode, falls back to flat ReadCellChildren (must visit <f>).
    // Postcondition: reader is on </c> EndElement.
    private static ExcelCellValue ReadCellValueNested(
        XmlReader reader,
        XlsxNameTable names,
        char[] valueBuf,
        CellDataKind kind,
        int styleIndex,
        bool readFormulas,
        string[] sharedStrings,
        StyleTable styles,
        bool isDate1904,
        ExcelReadMode mode)
    {
        // Formula mode: flat loop must visit both <f> and <v>.
        if (readFormulas)
        {
            ReadCellChildren(reader, names, valueBuf, readFormulas: true,
                out int fLen, out string? fOverflow, out string? inlineStr, out string? formulaText);
            // ReadCellChildren exits on </c>
            return DecodeFromSpan(valueBuf.AsSpan(0, fLen), fOverflow,
                kind, styleIndex, inlineStr, formulaText, sharedStrings, styles, isDate1904, mode);
        }

        if (kind == CellDataKind.InlineString)
        {
            string? inlineString = null;
            while (reader.Read())
            {
                if (reader.NodeType == XmlNodeType.EndElement && ReferenceEquals(reader.LocalName, names.C))
                {
                    break;
                }

                if (reader.NodeType == XmlNodeType.Element && ReferenceEquals(reader.LocalName, names.Is))
                {
                    inlineString = ReadInlineString(reader, names);
                    DrainToCellEnd(reader, names);
                    break;
                }
            }
            return inlineString is not null ? ExcelCellValue.FromText(inlineString) : ExcelCellValue.Empty;
        }

        while (reader.Read())
        {
            if (reader.NodeType == XmlNodeType.EndElement && ReferenceEquals(reader.LocalName, names.C))
            {
                return ExcelCellValue.Empty;
            }

            if (reader.NodeType != XmlNodeType.Element || !ReferenceEquals(reader.LocalName, names.V))
            {
                continue;
            }

            int valueLen = ReadValueIntoBuffer(reader, valueBuf, out string? valueOverflow);
            DrainToCellEnd(reader, names);
            return DecodeFromSpan(valueBuf.AsSpan(0, valueLen), valueOverflow,
                kind, styleIndex, inlineString: null, formulaText: null, sharedStrings, styles, isDate1904, mode);
        }

        return ExcelCellValue.Empty;
    }

    // Advances the reader from the current position (anywhere inside <c>) to the </c> EndElement.
    // Prerequisite: reader is inside the <c> subtree (e.g. on </v> or </is>).
    // Postcondition: reader is on </c> EndElement.
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void DrainToCellEnd(XmlReader reader, XlsxNameTable names)
    {
        while (reader.Read())
        {
            if (reader.NodeType == XmlNodeType.EndElement && ReferenceEquals(reader.LocalName, names.C))
            {
                break;
            }
        }
    }

    // Span-based decode — avoids string allocation for the dominant cell types
    // (Number, SharedString index, Boolean). Falls back to string for rare types.
    private static ExcelCellValue DecodeFromSpan(
        ReadOnlySpan<char> span,
        string? overflow,
        CellDataKind kind,
        int styleIndex,
        string? inlineString,
        string? formulaText,
        string[] sharedStrings,
        StyleTable styles,
        bool isDate1904,
        ExcelReadMode mode)
    {
        if (mode == ExcelReadMode.Formulas && formulaText is not null)
        {
            return ExcelCellValue.FromFormula(formulaText);
        }

        switch (kind)
        {
            case CellDataKind.SharedString:
                return DecodeSharedStringFromSpan(span, overflow, sharedStrings);

            case CellDataKind.Boolean:
                return (!span.IsEmpty || overflow is not null)
                    ? ExcelCellValue.FromBoolean((overflow is not null ? overflow[0] : span[0]) == '1')
                    : ExcelCellValue.Empty;

            case CellDataKind.InlineString:
                return inlineString is not null ? ExcelCellValue.FromText(inlineString) : ExcelCellValue.Empty;

            case CellDataKind.Error:
            {
                if (span.IsEmpty && overflow is null)
                {
                    return ExcelCellValue.Empty;
                }

                string errStr = overflow ?? new string(span);
                return ExcelCellValue.FromError(errStr);
            }

            case CellDataKind.FormulaString:
            {
                if (span.IsEmpty && overflow is null)
                {
                    return ExcelCellValue.Empty;
                }

                string fStr = overflow ?? new string(span);
                return ExcelCellValue.FromText(fStr);
            }

            default: // Number
                return DecodeNumberFromSpan(span, overflow, styleIndex, styles, isDate1904);
        }
    }

    private static ExcelCellValue DecodeSharedStringFromSpan(
        ReadOnlySpan<char> span, string? overflow, string[] sharedStrings)
    {
        // Empty <v> with t="s" MUST return Empty, NOT sharedStrings[0] (calamine #607)
        if (span.IsEmpty && overflow is null)
        {
            return ExcelCellValue.Empty;
        }

        ReadOnlySpan<char> src = overflow is not null ? overflow.AsSpan() : span;
        if (!int.TryParse(src, NumberStyles.None, CultureInfo.InvariantCulture, out int idx))
        {
            return ExcelCellValue.Empty;
        }

        return (uint)idx < (uint)sharedStrings.Length
            ? ExcelCellValue.FromText(sharedStrings[idx])
            : ExcelCellValue.Empty;
    }

    private static ExcelCellValue DecodeNumberFromSpan(
        ReadOnlySpan<char> span, string? overflow, int styleIndex, StyleTable styles, bool isDate1904)
    {
        if (span.IsEmpty && overflow is null)
        {
            return ExcelCellValue.Empty;
        }

        ReadOnlySpan<char> src = overflow is not null ? overflow.AsSpan() : span;
        if (!double.TryParse(src, NumberStyles.Float, CultureInfo.InvariantCulture, out double d))
        {
            return ExcelCellValue.Empty;
        }

        var formatClass = styles.GetClassification(styleIndex);
        if (formatClass is FormatClass.Date or FormatClass.DateTime or FormatClass.Time)
        {
            var dt = ExcelDateConverter.FromSerial(d, isDate1904);
            if (dt is not null)
            {
                return ExcelCellValue.FromDate(dt.Value);
            }
        }

        return ExcelCellValue.FromNumber(d);
    }

    // True for elements that are transparent containers for things we care about
    // (must be entered, not skipped). Everything else can be skipped wholesale.
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool IsTransparentElement(string localName, XlsxNameTable names) =>
        ReferenceEquals(localName, names.Worksheet) ||
        ReferenceEquals(localName, names.SheetData) ||
        ReferenceEquals(localName, names.MergeCells);

    // Read r=, s=, t= attributes in one pass rather than three separate GetAttribute
    // calls that each re-iterate the attribute list.
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void ReadCellAttributes(XmlReader reader, XlsxNameTable names,
        out string? cellRef, out string? styleStr, out string? typeStr)
    {
        cellRef = null; styleStr = null; typeStr = null;
        if (!reader.MoveToFirstAttribute()) { return; }
        do
        {
            var a = reader.LocalName;
            if (ReferenceEquals(a, names.R))      { cellRef  = reader.Value; }
            else if (ReferenceEquals(a, names.S)) { styleStr = reader.Value; }
            else if (ReferenceEquals(a, names.T)) { typeStr  = reader.Value; }
        }
        while (reader.MoveToNextAttribute());
        reader.MoveToElement();
    }

    private static ParsedCell ParseCell(XmlReader reader, XlsxNameTable names, char[] valueBuf, bool readFormulas)
    {
        ReadCellAttributes(reader, names, out string? cellRef, out string? styleStr, out string? typeStr);

        int row = 0, col = 0;
        if (cellRef is not null && CellReferenceParser.TryParse(cellRef, out var addr))
        {
            row = addr.Row;
            col = addr.Column;
        }

        int styleIndex = 0;
        if (styleStr is not null)
        {
            int.TryParse(styleStr, NumberStyles.None, CultureInfo.InvariantCulture, out styleIndex);
        }

        CellDataKind kind = typeStr switch
        {
            "s"         => CellDataKind.SharedString,
            "b"         => CellDataKind.Boolean,
            "inlineStr" => CellDataKind.InlineString,
            "str"       => CellDataKind.FormulaString,
            "e"         => CellDataKind.Error,
            _           => CellDataKind.Number,
        };

        string? rawValue = null;
        string? inlineString = null;
        string? formulaText = null;

        if (!reader.IsEmptyElement)
        {
            ReadCellChildren(reader, names, valueBuf, readFormulas,
                out int valueLen, out string? valueOverflow,
                out inlineString, out formulaText);
            rawValue = valueOverflow ?? (valueLen > 0 ? new string(valueBuf, 0, valueLen) : null);
        }

        return new ParsedCell(row, col, styleIndex, kind, rawValue, inlineString, formulaText);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool AtCellEnd(XmlReader reader, XlsxNameTable names) =>
        reader.NodeType == XmlNodeType.EndElement && ReferenceEquals(reader.LocalName, names.C);

    private static void ReadCellChildren(
        XmlReader reader,
        XlsxNameTable names,
        char[] valueBuf,
        bool readFormulas,
        out int valueLen,
        out string? valueOverflow,
        out string? inlineString,
        out string? formulaText)
    {
        valueLen = 0; valueOverflow = null; inlineString = null; formulaText = null;
        // skipNextRead: Skip()/ReadElementContentAsString() already advanced past the end
        // tag to the next sibling — suppress the next Read() call.
        bool skipNextRead = false;
        while (skipNextRead || reader.Read())
        {
            skipNextRead = false;
            if (AtCellEnd(reader, names)) { break; }
            if (reader.NodeType != XmlNodeType.Element) { continue; }
            if (ReferenceEquals(reader.LocalName, names.V))
            {
                valueLen = ReadValueIntoBuffer(reader, valueBuf, out valueOverflow);
            }
            else if (ReferenceEquals(reader.LocalName, names.F))
            {
                if (readFormulas)
                {
                    formulaText = reader.ReadElementContentAsString();
                    if (AtCellEnd(reader, names)) { break; }
                }
                else
                {
                    reader.Skip();
                    if (AtCellEnd(reader, names)) { break; }
                }
                skipNextRead = true;
            }
            else if (ReferenceEquals(reader.LocalName, names.Is))
            {
                inlineString = ReadInlineString(reader, names);
            }
            else
            {
                reader.Skip();
                if (AtCellEnd(reader, names)) { break; }
                skipNextRead = true;
            }
        }
    }

    // Returns the number of chars written to buf.
    // If the value exceeded buf.Length, overflow contains the full string and buf holds a partial chunk.
    private static int ReadValueIntoBuffer(XmlReader reader, char[] buf, out string? overflow)
    {
        overflow = null;
        if (reader.IsEmptyElement)
        {
            return 0;
        }

        int total = 0;
        StringBuilder? sb = null;

        while (reader.Read())
        {
            if (reader.NodeType is XmlNodeType.Text or XmlNodeType.CDATA)
            {
                // Drain this text node completely via ReadValueChunk loop.
                // If buf fills up, spill into a StringBuilder.
                int read;
                while (true)
                {
                    int available = buf.Length - total;
                    if (available > 0)
                    {
                        read = reader.ReadValueChunk(buf, total, available);
                        if (read == 0)
                        {
                            break;
                        }
                        total += read;
                    }
                    else
                    {
                        // Buffer exhausted — switch to StringBuilder for the remainder
                        if (sb is null)
                        {
                            sb = new StringBuilder(buf.Length * 2);
                            sb.Append(buf, 0, total);
                        }
                        read = reader.ReadValueChunk(buf, 0, buf.Length);
                        if (read == 0)
                        {
                            break;
                        }
                        sb.Append(buf, 0, read);
                    }
                }
            }
            else if (reader.NodeType == XmlNodeType.EndElement)
            {
                break;
            }
        }

        if (sb is not null)
        {
            overflow = sb.ToString();
            return overflow.Length;
        }

        return total;
    }

    private static string ReadInlineString(XmlReader reader, XlsxNameTable names)
    {
        if (reader.IsEmptyElement)
        {
            return string.Empty;
        }

        string? first = null;
        StringBuilder? sb = null;

        while (reader.Read())
        {
            if (reader.NodeType == XmlNodeType.EndElement
                && ReferenceEquals(reader.LocalName, names.Is))
            {
                break;
            }

            if (reader.NodeType == XmlNodeType.Element
                && ReferenceEquals(reader.LocalName, names.T))
            {
                var text = reader.ReadElementContentAsString();
                if (first is null)
                {
                    first = text;
                }
                else
                {
                    (sb ??= new StringBuilder(first)).Append(text);
                }

                // ReadElementContentAsString may advance past </t> directly to </is>
                if (reader.NodeType == XmlNodeType.EndElement
                    && ReferenceEquals(reader.LocalName, names.Is))
                {
                    break;
                }
            }
        }

        return sb?.ToString() ?? first ?? string.Empty;
    }
}
