using XLSight.Analysis;
using XLSight.Internal.Metadata;
using XLSight.Internal.Sinks;

namespace XLSight.Internal.Readers.Xlsb;

internal static class XlsbWorksheetScanner
{
    internal static void ScanSheet<TSink>(
        Stream worksheetStream,
        Lazy<XlsbSharedStringTable> sharedStrings,
        StyleTable styles,
        bool isDate1904,
        ReadMode mode,
        ExcelRange range,
        ref TSink sink,
        XlsbFormulaContext? formulaContext = null,
        bool includePostSheetMetadata = true,
        CancellationToken ct = default)
        where TSink : struct, IByteSheetSink
    {
        using var iterator = new XlsbRecordIterator(worksheetStream);
        int currentRowIndex = 0;
        bool shouldScanExactMetadata = range.IsUnbounded && includePostSheetMetadata;

        while (iterator.TryRead(out XlsbRecord record))
        {
            // Fast path: cell records (BrtCellBlank=1 through BrtFmlaError=11) never match
            // TryHandleExactMetadata or TryHandleRowHeader, so skip both checks.
            if ((uint)(record.Type - XlsbRecordType.BrtCellBlank) <=
                (uint)(XlsbRecordType.BrtFmlaError - XlsbRecordType.BrtCellBlank))
            {
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
                continue;
            }

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
                ct.ThrowIfCancellationRequested();
            }
        }

        ct.ThrowIfCancellationRequested();
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
                if (XlsbBinary.TryReadRfx(record.Payload) is { } dimension)
                {
                    sink.OnDimension(dimension);
                }
                return true;

            case XlsbRecordType.BrtMergeCell when includePostSheetData:
                if (XlsbBinary.TryReadRfx(record.Payload) is { } merged)
                {
                    sink.OnMergeCell(new MergedRegion(
                        merged.TopLeft.Row,
                        merged.TopLeft.Column,
                        merged.BottomRight.Row,
                        merged.BottomRight.Column));
                }
                return true;

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
        if (record.Type != XlsbRecordType.BrtRowHdr)
        {
            stop = false;
            return false;
        }

        rowIndex = XlsbBinary.ReadRowIndex(record.Payload);
        if (rowIndex <= 0)
        {
            stop = false;
            return true;
        }

        stop = !range.IsUnbounded && rowIndex > range.BottomRight.Row;
        if (!stop && IsRowInRange(rowIndex, range))
        {
            sink.OnRowStart(rowIndex);
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
        if (rowIndex <= 0 || !IsRowInRange(rowIndex, range))
        {
            return true;
        }

        if (!XlsbCellDecoder.TryDecodeForSink(
                record,
                sharedStrings,
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
                out bool isFormula,
                out ReadOnlySpan<byte> formulaSpan))
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

        if (isFormula && sink.TracksFormulaReferences && formulaContext is not null && !formulaSpan.IsEmpty)
        {
            XlsbFormulaDecoder.EmitReferences(formulaSpan, formulaContext, ref sink);
        }

        return sink.OnCell(columnIndex, kind, styleIndex, value, rawIndex);
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
