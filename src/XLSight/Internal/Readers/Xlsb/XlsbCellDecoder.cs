using XLSight.Internal.Metadata;
using XLSight.Internal.Sinks;

namespace XLSight.Internal.Readers.Xlsb;

internal static class XlsbCellDecoder
{
    private const int CellHeaderLength = 8;

    internal static bool TryDecode(
        XlsbRecord record,
        Lazy<XlsbSharedStringTable> sharedStrings,
        StyleTable styles,
        bool isDate1904,
        ReadMode mode,
        XlsbFormulaContext? formulaContext,
        out int columnIndex,
        out ExcelCellValue value) => TryDecodeForSink(
            record,
            sharedStrings,
            styles,
            isDate1904,
            mode,
            decodeSharedString: true,
            formulaContext,
            out columnIndex,
            out _,
            out _,
            out value,
            out _,
            out _,
            out _);

    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
    internal static bool TryDecodeCommonValue(
        XlsbRecord record,
        Lazy<XlsbSharedStringTable> sharedStrings,
        StyleTable styles,
        bool isDate1904,
        out int columnIndex,
        out ExcelCellValue value)
    {
        ReadOnlySpan<byte> payload = record.Payload;
        if (!TryReadCellHeader(payload, out columnIndex, out int styleIndex))
        {
            value = ExcelCellValue.Empty;
            return false;
        }

        ReadOnlySpan<byte> data = payload[CellHeaderLength..];
        switch (record.Type)
        {
            case XlsbRecordType.BrtCellRk:
                value = DecodeRk(data, styleIndex, styles, isDate1904);
                return true;
            case XlsbRecordType.BrtCellReal:
                value = data.Length >= 8
                    ? DecodeNumber(XlsbBinary.ReadDouble(data, 0), styleIndex, styles, isDate1904)
                    : ExcelCellValue.Empty;
                return true;
            case XlsbRecordType.BrtCellIsst:
                if (data.Length >= 4)
                {
                    int index = XlsbBinary.ReadInt32(data, 0);
                    value = index >= 0
                        ? ExcelCellValue.FromSharedString(sharedStrings.Value.GetString(index), index)
                        : ExcelCellValue.Empty;
                }
                else
                {
                    value = ExcelCellValue.Empty;
                }
                return true;
            default:
                value = ExcelCellValue.Empty;
                return false;
        }
    }

    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
    internal static bool TryDecodeForSink(
        XlsbRecord record,
        Lazy<XlsbSharedStringTable> sharedStrings,
        StyleTable styles,
        bool isDate1904,
        ReadMode mode,
        bool decodeSharedString,
        XlsbFormulaContext? formulaContext,
        out int columnIndex,
        out CellDataKind kind,
        out int styleIndex,
        out ExcelCellValue value,
        out int rawIndex,
        out bool isFormula,
        out ReadOnlySpan<byte> formulaSpan)
    {
        columnIndex = 0;
        kind = CellDataKind.Number;
        styleIndex = 0;
        value = ExcelCellValue.Empty;
        rawIndex = -1;
        formulaSpan = [];
        isFormula = IsFormulaRecord(record.Type);

        ReadOnlySpan<byte> payload = record.Payload;
        if (!TryReadCellHeader(payload, out columnIndex, out styleIndex))
        {
            return false;
        }

        ReadOnlySpan<byte> data = payload[CellHeaderLength..];
        if (isFormula)
        {
            formulaSpan = SliceFormulaBytes(record.Type, data);
        }

        if (mode == ReadMode.Formulas && isFormula)
        {
            kind = CellDataKind.FormulaString;
            value = ExcelCellValue.FromFormula(
                formulaSpan.IsEmpty ? string.Empty : XlsbFormulaDecoder.Decode(formulaSpan, formulaContext));
            return true;
        }

        return TryApplyRecordValue(record, data, styleIndex, sharedStrings, styles, isDate1904, decodeSharedString,
            out kind, out value, out rawIndex);
    }

    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
    private static bool TryApplyRecordValue(
        XlsbRecord record,
        ReadOnlySpan<byte> data,
        int styleIndex,
        Lazy<XlsbSharedStringTable> sharedStrings,
        StyleTable styles,
        bool isDate1904,
        bool decodeSharedString,
        out CellDataKind kind,
        out ExcelCellValue value,
        out int rawIndex)
    {
        kind = CellDataKind.Number;
        value = ExcelCellValue.Empty;
        rawIndex = -1;
        switch (record.Type)
        {
            case XlsbRecordType.BrtCellBlank:
                return true;
            case XlsbRecordType.BrtCellRk:
                value = DecodeRk(data, styleIndex, styles, isDate1904);
                return true;
            case XlsbRecordType.BrtCellReal:
            case XlsbRecordType.BrtFmlaNum:
                value = data.Length >= 8
                    ? DecodeNumber(XlsbBinary.ReadDouble(data, 0), styleIndex, styles, isDate1904)
                    : ExcelCellValue.Empty;
                return true;
            case XlsbRecordType.BrtCellBool:
            case XlsbRecordType.BrtFmlaBool:
                kind = CellDataKind.Boolean;
                value = data.IsEmpty ? ExcelCellValue.Empty : ExcelCellValue.FromBoolean(data[0] != 0);
                return true;
            case XlsbRecordType.BrtCellError:
            case XlsbRecordType.BrtFmlaError:
                kind = CellDataKind.Error;
                value = data.IsEmpty
                    ? ExcelCellValue.Empty
                    : ExcelCellValue.FromError(XlsbFormulaDecoder.GetErrorText(data[0]));
                return true;
            case XlsbRecordType.BrtCellSt:
                kind = CellDataKind.InlineString;
                value = DecodeString(data);
                return true;
            case XlsbRecordType.BrtCellIsst:
                kind = CellDataKind.SharedString;
                return TryDecodeSharedStringForSink(data, sharedStrings, decodeSharedString, out value, out rawIndex);
            case XlsbRecordType.BrtFmlaString:
                kind = CellDataKind.FormulaString;
                value = DecodeString(data);
                return true;
            default:
                return false;
        }
    }

    internal static bool TryReadCellLocation(ReadOnlySpan<byte> payload, out int columnIndex) =>
        TryReadCellHeader(payload, out columnIndex, out _);

    internal static bool TryReadCellHeader(
        ReadOnlySpan<byte> payload,
        out int columnIndex,
        out int styleIndex)
    {
        columnIndex = 0;
        styleIndex = 0;

        if (payload.Length < CellHeaderLength)
        {
            return false;
        }

        columnIndex = checked(XlsbBinary.ReadInt32(payload, 0) + 1);
        styleIndex = payload[4] | (payload[5] << 8) | (payload[6] << 16);
        return columnIndex is > 0 and <= ExcelLimits.MaxColumns;
    }

    private static ExcelCellValue DecodeRk(
        ReadOnlySpan<byte> data,
        int styleIndex,
        StyleTable styles,
        bool isDate1904)
    {
        if (data.Length < 4)
        {
            return ExcelCellValue.Empty;
        }

        uint encoded = XlsbBinary.ReadUInt32(data, 0);
        double number;
        if ((encoded & 0x02u) != 0)
        {
            number = (int)encoded >> 2;
        }
        else
        {
            long bits = (long)(encoded & 0xFFFFFFFCu) << 32;
            number = BitConverter.Int64BitsToDouble(bits);
        }

        if ((encoded & 0x01u) != 0)
        {
            number /= 100.0;
        }

        return DecodeNumber(number, styleIndex, styles, isDate1904);
    }

    private static ExcelCellValue DecodeNumber(
        double number,
        int styleIndex,
        StyleTable styles,
        bool isDate1904)
    {
        var classification = styles.GetClassification(styleIndex);
        if (classification is FormatClass.Date or FormatClass.DateTime or FormatClass.Time)
        {
            DateTime? date = ExcelDateConverter.FromSerial(number, isDate1904);
            return date.HasValue ? ExcelCellValue.FromDate(date.Value) : ExcelCellValue.Empty;
        }

        return ExcelCellValue.FromNumber(number);
    }

    private static ExcelCellValue DecodeString(ReadOnlySpan<byte> data) =>
        TryDecodeWideString(data, out string value)
            ? ExcelCellValue.FromText(value)
            : ExcelCellValue.Empty;

    private static bool TryDecodeWideString(ReadOnlySpan<byte> data, out string value)
    {
        value = string.Empty;
        if (data.Length < 4)
        {
            return false;
        }

        uint charCount = XlsbBinary.ReadUInt32(data, 0);
        if (charCount > int.MaxValue / 2 || charCount > (uint)((data.Length - 4) / 2))
        {
            return false;
        }

        try
        {
            int offset = 0;
            value = XlsbBinary.ReadWideString(data, ref offset);
            return true;
        }
        catch (Exception ex) when (ex is MalformedWorkbookException or OverflowException)
        {
            return false;
        }
    }

    internal static bool TryGetFormula(XlsbRecord record, out ReadOnlySpan<byte> formula)
    {
        formula = [];
        ReadOnlySpan<byte> payload = record.Payload;
        if (!IsFormulaRecord(record.Type) || payload.Length < CellHeaderLength)
        {
            return false;
        }

        formula = SliceFormulaBytes(record.Type, payload[CellHeaderLength..]);
        return !formula.IsEmpty;
    }

    private static ReadOnlySpan<byte> SliceFormulaBytes(int recordType, ReadOnlySpan<byte> data)
    {
        int offset = GetFormulaOffset(recordType, data);
        return offset >= 0 && offset < data.Length ? data[offset..] : [];
    }

    private static int GetFormulaOffset(int recordType, ReadOnlySpan<byte> data)
    {
        const int flagsLength = 2;
        return recordType switch
        {
            XlsbRecordType.BrtFmlaNum => data.Length >= 10 ? 8 + flagsLength : -1,
            XlsbRecordType.BrtFmlaBool or XlsbRecordType.BrtFmlaError => data.Length >= 3 ? 1 + flagsLength : -1,
            XlsbRecordType.BrtFmlaString => GetFormulaStringOffset(data),
            _ => -1,
        };
    }

    private static int GetFormulaStringOffset(ReadOnlySpan<byte> data)
    {
        if (data.Length < 6)
        {
            return -1;
        }

        int charCount = XlsbBinary.ReadInt32(data, 0);
        if (charCount < 0)
        {
            return -1;
        }

        if (charCount > (data.Length - 6) / 2)
        {
            return -1;
        }

        int byteCount = charCount * 2;
        int offset = 4 + byteCount + 2;
        return offset <= data.Length ? offset : -1;
    }

    private static bool TryDecodeSharedStringForSink(
        ReadOnlySpan<byte> data,
        Lazy<XlsbSharedStringTable> sharedStrings,
        bool decode,
        out ExcelCellValue value,
        out int rawIndex)
    {
        value = ExcelCellValue.Empty;
        rawIndex = -1;

        if (data.Length < 4)
        {
            return true;
        }

        rawIndex = XlsbBinary.ReadInt32(data, 0);
        if (rawIndex < 0)
        {
            rawIndex = -1;
            return true;
        }

        if (decode)
        {
            value = ExcelCellValue.FromSharedString(sharedStrings.Value.GetString(rawIndex), rawIndex);
        }

        return true;
    }

    private static bool IsFormulaRecord(int recordType) =>
        (uint)(recordType - XlsbRecordType.BrtFmlaString) <=
        (uint)(XlsbRecordType.BrtFmlaError - XlsbRecordType.BrtFmlaString);

}
