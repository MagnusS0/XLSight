using System.Buffers;
using XLSight.Analysis;
using XLSight.Internal.Metadata;
using XLSight.Internal.Sinks;

namespace XLSight.Internal.Readers.Xlsb;

internal static class XlsbWorksheetScanner
{
    internal static IEnumerable<ExcelRow> ScanRows(
        Stream worksheetStream,
        XlsbSharedStringTable sharedStrings,
        StyleTable styles,
        bool isDate1904,
        ReadMode mode,
        ExcelRange range)
        => ScanRows(
            worksheetStream,
            new Lazy<XlsbSharedStringTable>(() => sharedStrings, LazyThreadSafetyMode.PublicationOnly),
            styles,
            isDate1904,
            mode,
            range,
            formulaContext: null);

    internal static IEnumerable<ExcelRow> ScanRows(
        Stream worksheetStream,
        Lazy<XlsbSharedStringTable> sharedStrings,
        StyleTable styles,
        bool isDate1904,
        ReadMode mode,
        ExcelRange range,
        XlsbFormulaContext? formulaContext = null)
    {
        var cellPool = ArrayPool<ExcelCellValue>.Shared.Rent(ExcelLimits.MaxColumns);
        using var iterator = new XlsbRecordIterator(worksheetStream);
        using var scanner = new XlsbRowScanner(
            iterator,
            sharedStrings,
            styles,
            isDate1904,
            mode,
            range,
            cellPool,
            formulaContext);

        try
        {
            while (scanner.MoveNext())
            {
                var current = scanner.Current;
                var cells = new ExcelCellValue[current.CellCount];
                current.Cells.CopyTo(cells);
                yield return new ExcelRow(current.RowIndex, cells, current.StartColumn);
            }
        }
        finally
        {
            ArrayPool<ExcelCellValue>.Shared.Return(cellPool, clearArray: false);
        }
    }

    internal static XlsbSheetCursor OpenCursor(
        Stream worksheetStream,
        XlsbSharedStringTable sharedStrings,
        StyleTable styles,
        bool isDate1904,
        ReadMode mode,
        ExcelRange range)
        => OpenCursor(
            worksheetStream,
            new Lazy<XlsbSharedStringTable>(() => sharedStrings, LazyThreadSafetyMode.PublicationOnly),
            styles,
            isDate1904,
            mode,
            range,
            formulaContext: null);

    internal static XlsbSheetCursor OpenCursor(
        Stream worksheetStream,
        Lazy<XlsbSharedStringTable> sharedStrings,
        StyleTable styles,
        bool isDate1904,
        ReadMode mode,
        ExcelRange range,
        XlsbFormulaContext? formulaContext = null)
        => new(worksheetStream, sharedStrings, styles, isDate1904, mode, range, formulaContext);

    internal static void ScanSheet<TSink>(
        Stream worksheetStream,
        XlsbSharedStringTable sharedStrings,
        StyleTable styles,
        bool isDate1904,
        ReadMode mode,
        ExcelRange range,
        ref TSink sink)
        where TSink : struct, IByteSheetSink
        => ScanSheet(
            worksheetStream,
            new Lazy<XlsbSharedStringTable>(() => sharedStrings, LazyThreadSafetyMode.PublicationOnly),
            styles,
            isDate1904,
            mode,
            range,
            formulaContext: null,
            ref sink);

    internal static void ScanSheet<TSink>(
        Stream worksheetStream,
        Lazy<XlsbSharedStringTable> sharedStrings,
        StyleTable styles,
        bool isDate1904,
        ReadMode mode,
        ExcelRange range,
        ref TSink sink)
        where TSink : struct, IByteSheetSink
        => ScanSheet(
            worksheetStream,
            sharedStrings,
            styles,
            isDate1904,
            mode,
            range,
            formulaContext: null,
            ref sink);

    internal static void ScanSheet<TSink>(
        Stream worksheetStream,
        Lazy<XlsbSharedStringTable> sharedStrings,
        StyleTable styles,
        bool isDate1904,
        ReadMode mode,
        ExcelRange range,
        XlsbFormulaContext? formulaContext,
        ref TSink sink)
        where TSink : struct, IByteSheetSink
    {
        using var iterator = new XlsbRecordIterator(worksheetStream);
        int currentRowIndex = 0;
        bool shouldScanExactMetadata = range.IsUnbounded;

        while (iterator.TryRead(out XlsbRecord record))
        {
            if (record.Type == XlsbRecordType.BrtEndSheetData)
            {
                if (!shouldScanExactMetadata)
                {
                    break;
                }

                currentRowIndex = 0;
                continue;
            }

            if (TryHandleExactMetadata(record, shouldScanExactMetadata, formulaContext, ref sink))
            {
                continue;
            }

            if (TryHandleRowHeader(record, range, ref currentRowIndex, ref sink, out bool stop))
            {
                if (stop) { break; }
                continue;
            }

            if (!TryPushCell(
                    record,
                    currentRowIndex,
                    sharedStrings,
                    styles,
                    isDate1904,
                    mode,
                    range,
                    formulaContext,
                    ref sink))
            {
                break;
            }
        }

        sink.OnEnd();
    }

    private static bool TryHandleExactMetadata<TSink>(
        XlsbRecord record,
        bool includePostSheetData,
        XlsbFormulaContext? formulaContext,
        ref TSink sink)
        where TSink : struct, IByteSheetSink
    {
        switch (record.Type)
        {
            case XlsbRecordType.BrtWsDim:
            {
                if (TryReadRange(record.Payload, out int startRow, out int startColumn, out int endRow, out int endColumn))
                {
                    sink.OnDimension(new ExcelRange(
                        new ExcelAddress(startColumn, startRow),
                        new ExcelAddress(endColumn, endRow)));
                }

                return true;
            }

            case XlsbRecordType.BrtMergeCell when includePostSheetData:
            {
                if (TryReadRange(record.Payload, out int startRow, out int startColumn, out int endRow, out int endColumn))
                {
                    sink.OnMergeCell(new MergedRegion(startRow, startColumn, endRow, endColumn));
                }

                return true;
            }

            case XlsbRecordType.BrtBeginConditionalFormatting when includePostSheetData:
            case XlsbRecordType.BrtBeginConditionalFormatting14 when includePostSheetData:
                sink.OnConditionalFormatting();
                return true;

            case XlsbRecordType.BrtDVal when includePostSheetData:
                sink.OnDataValidation(XlsbDataValidationParser.Parse(record.Payload, formulaContext));
                return true;

            case XlsbRecordType.BrtDVal14 when includePostSheetData:
                sink.OnDataValidation(null);
                return true;

            case XlsbRecordType.BrtHLink when includePostSheetData:
                sink.OnHyperlink();
                return true;

            default:
                return false;
        }
    }

    private static bool TryHandleRowHeader<TSink>(
        XlsbRecord record,
        ExcelRange range,
        ref int rowIndex,
        ref TSink sink,
        out bool stop)
        where TSink : struct, IByteSheetSink
    {
        stop = false;
        if (record.Type != XlsbRecordType.BrtRowHdr)
        {
            return false;
        }

        int nextRowIndex = XlsbBinary.ReadRowIndex(record.Payload);
        if (nextRowIndex <= 0)
        {
            rowIndex = 0;
            return true;
        }

        rowIndex = nextRowIndex;
        if (!range.IsUnbounded && nextRowIndex > range.BottomRight.Row)
        {
            stop = true;
            return true;
        }

        if (IsRowInRange(nextRowIndex, range))
        {
            sink.OnRowStart(nextRowIndex);
        }

        return true;
    }

    private static bool TryPushCell<TSink>(
        XlsbRecord record,
        int rowIndex,
        Lazy<XlsbSharedStringTable> sharedStrings,
        StyleTable styles,
        bool isDate1904,
        ReadMode mode,
        ExcelRange range,
        XlsbFormulaContext? formulaContext,
        ref TSink sink)
        where TSink : struct, IByteSheetSink
    {
        if (rowIndex <= 0 || !IsRowInRange(rowIndex, range) || !IsSupportedCellRecord(record.Type))
        {
            return true;
        }

        if (!XlsbCellDecoder.TryDecodeForSink(
                record,
                index => sharedStrings.Value.GetString(index),
                styles,
                isDate1904,
                mode,
                sink.NeedsDecodedValue,
                formulaContext,
                out int columnIndex,
                out CellDataKind kind,
                out int styleIndex,
                out ExcelCellValue value,
                out int rawIndex,
                out bool isFormula))
        {
            return true;
        }

        if (!IsColumnInRange(columnIndex, range))
        {
            return true;
        }

        if (isFormula && sink.TracksFormulas)
        {
            sink.OnFormula(columnIndex, isArray: false);
        }

        if (isFormula && sink.TracksFormulaReferences && formulaContext is not null &&
            XlsbCellDecoder.TryGetFormula(record, out ReadOnlySpan<byte> formula))
        {
            XlsbFormulaDecoder.EmitReferences(formula, formulaContext, ref sink);
        }

        return sink.OnCell(columnIndex, kind, styleIndex, value, rawIndex);
    }

    private static bool IsSupportedCellRecord(int recordType) => recordType
        is XlsbRecordType.BrtCellBlank
        or XlsbRecordType.BrtCellRk
        or XlsbRecordType.BrtCellError
        or XlsbRecordType.BrtCellBool
        or XlsbRecordType.BrtCellReal
        or XlsbRecordType.BrtCellSt
        or XlsbRecordType.BrtCellIsst
        or XlsbRecordType.BrtFmlaString
        or XlsbRecordType.BrtFmlaNum
        or XlsbRecordType.BrtFmlaBool
        or XlsbRecordType.BrtFmlaError;

    private static bool TryReadRange(
        ReadOnlySpan<byte> payload,
        out int startRow,
        out int startColumn,
        out int endRow,
        out int endColumn)
    {
        startRow = 0;
        startColumn = 0;
        endRow = 0;
        endColumn = 0;

        if (payload.Length < 16)
        {
            return false;
        }

        uint rowFirst = XlsbBinary.ReadUInt32(payload, 0);
        uint rowLast = XlsbBinary.ReadUInt32(payload, 4);
        uint columnFirst = XlsbBinary.ReadUInt32(payload, 8);
        uint columnLast = XlsbBinary.ReadUInt32(payload, 12);

        if (rowFirst > rowLast ||
            columnFirst > columnLast ||
            rowLast >= ExcelLimits.MaxRows ||
            columnLast >= ExcelLimits.MaxColumns)
        {
            return false;
        }

        startRow = checked((int)rowFirst + 1);
        endRow = checked((int)rowLast + 1);
        startColumn = checked((int)columnFirst + 1);
        endColumn = checked((int)columnLast + 1);
        return true;
    }

    private static bool IsRowInRange(int rowIndex, ExcelRange range) =>
        range.IsUnbounded ||
        rowIndex >= range.TopLeft.Row &&
        rowIndex <= range.BottomRight.Row;

    private static bool IsColumnInRange(int columnIndex, ExcelRange range) =>
        range.IsUnbounded ||
        columnIndex >= range.TopLeft.Column &&
        columnIndex <= range.BottomRight.Column;
}
