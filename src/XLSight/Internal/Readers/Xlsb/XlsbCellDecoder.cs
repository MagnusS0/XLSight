using XLSight.Internal.Metadata;
using XLSight.Internal.Sinks;

namespace XLSight.Internal.Readers.Xlsb;

internal static class XlsbCellDecoder
{
    private const int CellHeaderLength = 8;

    internal static bool TryDecode(
        XlsbRecord record,
        int currentRowIndex,
        Lazy<XlsbSharedStringTable> sharedStrings,
        StyleTable styles,
        bool isDate1904,
        ReadMode mode,
        XlsbFormulaContext? formulaContext,
        out int rowIndex,
        out int columnIndex,
        out ExcelCellValue value)
    {
        rowIndex = 0;
        columnIndex = 0;
        value = ExcelCellValue.Empty;

        if (currentRowIndex <= 0 ||
            !TryReadCellHeader(record.Payload, out columnIndex, out int styleIndex))
        {
            return false;
        }

        rowIndex = currentRowIndex;
        ReadOnlySpan<byte> data = record.Payload[CellHeaderLength..];
        if (mode == ReadMode.Formulas && IsFormulaRecord(record.Type))
        {
            value = ExcelCellValue.FromFormula(DecodeFormula(record.Type, data, formulaContext));
            return true;
        }

        value = record.Type switch
        {
            XlsbRecordType.BrtCellRk => DecodeRk(data, styleIndex, styles, isDate1904),
            XlsbRecordType.BrtCellReal => DecodeReal(data, styleIndex, styles, isDate1904),
            XlsbRecordType.BrtCellBool => DecodeBool(data),
            XlsbRecordType.BrtCellError => DecodeError(data),
            XlsbRecordType.BrtCellSt => DecodeInlineString(data),
            XlsbRecordType.BrtCellIsst => DecodeSharedString(data, sharedStrings),
            XlsbRecordType.BrtFmlaNum => DecodeFormulaNumber(data, styleIndex, styles, isDate1904),
            XlsbRecordType.BrtFmlaString => DecodeFormulaString(data),
            XlsbRecordType.BrtFmlaBool => DecodeBool(data),
            XlsbRecordType.BrtFmlaError => DecodeError(data),
            _ => ExcelCellValue.Empty,
        };

        return true;
    }

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

        if (!TryReadCellHeader(record.Payload, out columnIndex, out styleIndex))
        {
            return false;
        }

        ReadOnlySpan<byte> data = record.Payload[CellHeaderLength..];
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
                value = DecodeReal(data, styleIndex, styles, isDate1904);
                return true;
            case XlsbRecordType.BrtCellBool:
            case XlsbRecordType.BrtFmlaBool:
                kind = CellDataKind.Boolean;
                value = DecodeBool(data);
                return true;
            case XlsbRecordType.BrtCellError:
            case XlsbRecordType.BrtFmlaError:
                kind = CellDataKind.Error;
                value = DecodeError(data);
                return true;
            case XlsbRecordType.BrtCellSt:
                kind = CellDataKind.InlineString;
                value = DecodeInlineString(data);
                return true;
            case XlsbRecordType.BrtCellIsst:
                kind = CellDataKind.SharedString;
                return TryDecodeSharedStringForSink(data, sharedStrings, decodeSharedString, out value, out rawIndex);
            case XlsbRecordType.BrtFmlaNum:
                value = DecodeFormulaNumber(data, styleIndex, styles, isDate1904);
                return true;
            case XlsbRecordType.BrtFmlaString:
                kind = CellDataKind.FormulaString;
                value = DecodeFormulaString(data);
                return true;
            default:
                return false;
        }
    }

    internal static bool TryReadCellLocation(ReadOnlySpan<byte> payload, out int columnIndex)
    {
        columnIndex = 0;
        if (payload.Length < CellHeaderLength)
        {
            return false;
        }

        columnIndex = checked(XlsbBinary.ReadInt32(payload, 0) + 1);
        return columnIndex is > 0 and <= ExcelLimits.MaxColumns;
    }

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

    private static ExcelCellValue DecodeReal(
        ReadOnlySpan<byte> data,
        int styleIndex,
        StyleTable styles,
        bool isDate1904)
    {
        if (data.Length < 8)
        {
            return ExcelCellValue.Empty;
        }

        return DecodeNumber(XlsbBinary.ReadDouble(data, 0), styleIndex, styles, isDate1904);
    }

    private static ExcelCellValue DecodeFormulaNumber(
        ReadOnlySpan<byte> data,
        int styleIndex,
        StyleTable styles,
        bool isDate1904)
    {
        if (data.Length >= 8)
        {
            return DecodeNumber(XlsbBinary.ReadDouble(data, 0), styleIndex, styles, isDate1904);
        }

        return ExcelCellValue.Empty;
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

    private static ExcelCellValue DecodeBool(ReadOnlySpan<byte> data)
    {
        if (data.IsEmpty)
        {
            return ExcelCellValue.Empty;
        }

        return ExcelCellValue.FromBoolean(data[0] != 0);
    }

    private static ExcelCellValue DecodeError(ReadOnlySpan<byte> data)
    {
        if (data.IsEmpty)
        {
            return ExcelCellValue.Empty;
        }

        return ExcelCellValue.FromError(GetErrorText(data[0]));
    }

    private static ExcelCellValue DecodeInlineString(ReadOnlySpan<byte> data)
    {
        return TryDecodeWideString(data, out string value)
            ? ExcelCellValue.FromText(value)
            : ExcelCellValue.Empty;
    }

    private static ExcelCellValue DecodeFormulaString(ReadOnlySpan<byte> data)
    {
        return TryDecodeWideString(data, out string value)
            ? ExcelCellValue.FromText(value)
            : ExcelCellValue.Empty;
    }

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
        catch (MalformedWorkbookException)
        {
            return false;
        }
        catch (OverflowException)
        {
            return false;
        }
    }

    internal static bool TryGetFormula(XlsbRecord record, out ReadOnlySpan<byte> formula)
    {
        formula = [];
        if (!IsFormulaRecord(record.Type) || record.Payload.Length < CellHeaderLength)
        {
            return false;
        }

        ReadOnlySpan<byte> data = record.Payload[CellHeaderLength..];
        int formulaOffset = GetFormulaOffset(record.Type, data);
        if (formulaOffset < 0 || formulaOffset >= data.Length)
        {
            return false;
        }

        formula = data[formulaOffset..];
        return true;
    }

    private static string DecodeFormula(
        int recordType,
        ReadOnlySpan<byte> data,
        XlsbFormulaContext? formulaContext)
    {
        int formulaOffset = GetFormulaOffset(recordType, data);
        if (formulaOffset < 0 || formulaOffset >= data.Length)
        {
            return string.Empty;
        }

        return XlsbFormulaDecoder.Decode(data[formulaOffset..], formulaContext);
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

    private static ExcelCellValue DecodeSharedString(
        ReadOnlySpan<byte> data,
        Lazy<XlsbSharedStringTable> sharedStrings)
    {
        if (data.Length < 4)
        {
            return ExcelCellValue.Empty;
        }

        int index = XlsbBinary.ReadInt32(data, 0);
        if (index < 0)
        {
            return ExcelCellValue.Empty;
        }

        return ExcelCellValue.FromSharedString(sharedStrings.Value.GetString(index), index);
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

    private static bool IsFormulaRecord(int recordType) => recordType
        is XlsbRecordType.BrtFmlaString
        or XlsbRecordType.BrtFmlaNum
        or XlsbRecordType.BrtFmlaBool
        or XlsbRecordType.BrtFmlaError;

    private static string GetErrorText(byte errorCode) => errorCode switch
    {
        0x00 => "#NULL!",
        0x07 => "#DIV/0!",
        0x0F => "#VALUE!",
        0x17 => "#REF!",
        0x1D => "#NAME?",
        0x24 => "#NUM!",
        0x2A => "#N/A",
        0x2B => "#GETTING_DATA",
        _ => $"#ERR{errorCode}",
    };
}
