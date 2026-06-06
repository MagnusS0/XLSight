using XLSight.Analysis;

namespace XLSight.Internal.Readers.Xlsb;

internal static class XlsbDataValidationParser
{
    private const int FixedFlagsLength = 4;
    private const int RangeLength = 16;
    private const int MaxFormulaBytes = 16_384;

    internal static DataValidationInfo? Parse(
        ReadOnlySpan<byte> payload,
        XlsbFormulaContext? formulaContext)
    {
        if (payload.Length < FixedFlagsLength)
        {
            return null;
        }

        try
        {
            uint flags = XlsbBinary.ReadUInt32(payload, 0);
            int offset = FixedFlagsLength;
            string errorTitle = XlsbBinary.ReadNullableWideString(payload, ref offset);
            string errorMessage = XlsbBinary.ReadNullableWideString(payload, ref offset);
            string promptTitle = XlsbBinary.ReadNullableWideString(payload, ref offset);
            string promptMessage = XlsbBinary.ReadNullableWideString(payload, ref offset);

            string? formula1 = ReadFormula(payload, ref offset, formulaContext);
            string? formula2 = ReadFormula(payload, ref offset, formulaContext);
            List<ExcelRange> ranges = ReadRanges(payload, ref offset);
            if (ranges.Count == 0)
            {
                return null;
            }

            DataValidationType type = ParseType(flags);
            return new DataValidationInfo
            {
                Type = type,
                SequenceOfReferences = string.Join(' ', ranges.Select(FormatRange)),
                Ranges = ranges,
                Formula1 = formula1,
                Formula2 = formula2,
                Operator = UsesOperator(type) ? ParseOperator(flags) : null,
                AllowBlank = (flags & (1u << 8)) != 0,
                ShowDropDown = (flags & (1u << 9)) != 0,
                ShowInputMessage = (flags & (1u << 18)) != 0,
                ShowErrorMessage = (flags & (1u << 19)) != 0,
                ErrorStyle = ParseErrorStyle(flags),
                ErrorTitle = NullIfEmpty(errorTitle),
                ErrorMessage = NullIfEmpty(errorMessage),
                PromptTitle = NullIfEmpty(promptTitle),
                PromptMessage = NullIfEmpty(promptMessage),
            };
        }
        catch (MalformedWorkbookException)
        {
            return null;
        }
        catch (OverflowException)
        {
            return null;
        }
    }

    private static string? ReadFormula(
        ReadOnlySpan<byte> payload,
        ref int offset,
        XlsbFormulaContext? formulaContext)
    {
        int start = offset;
        if (payload.Length - offset < 8)
        {
            throw new MalformedWorkbookException("XLSB data-validation formula is truncated.");
        }

        uint tokenBytes = XlsbBinary.ReadUInt32(payload, offset);
        if (tokenBytes > MaxFormulaBytes || tokenBytes > (uint)(payload.Length - offset - 8))
        {
            throw new MalformedWorkbookException("XLSB data-validation formula is invalid.");
        }

        offset += 4 + (int)tokenBytes;
        uint extraBytes = XlsbBinary.ReadUInt32(payload, offset);
        offset += 4;
        if (extraBytes > (uint)(payload.Length - offset))
        {
            throw new MalformedWorkbookException("XLSB data-validation formula extra data is truncated.");
        }

        offset += (int)extraBytes;
        if (tokenBytes == 0)
        {
            return null;
        }

        string formula = XlsbFormulaDecoder.Decode(payload[start..offset], formulaContext);
        return NullIfEmpty(formula);
    }

    private static List<ExcelRange> ReadRanges(ReadOnlySpan<byte> payload, ref int offset)
    {
        if (payload.Length - offset < 4)
        {
            throw new MalformedWorkbookException("XLSB data-validation ranges are truncated.");
        }

        uint count = XlsbBinary.ReadUInt32(payload, offset);
        offset += 4;
        if (count > ExcelLimits.MaxCells || count > (uint)((payload.Length - offset) / RangeLength))
        {
            throw new MalformedWorkbookException("XLSB data-validation range count is invalid.");
        }

        var ranges = new List<ExcelRange>((int)Math.Min(count, 16));
        for (uint i = 0; i < count; i++)
        {
            ExcelRange? range = XlsbBinary.TryReadRfx(payload.Slice(offset, RangeLength));
            if (range.HasValue)
            {
                ranges.Add(range.Value);
            }

            offset += RangeLength;
        }

        return ranges;
    }

    private static DataValidationType ParseType(uint flags) => (flags & 0xFu) switch
    {
        <= 7 => (DataValidationType)(flags & 0xFu),
        _ => DataValidationType.None,
    };

    private static DataValidationOperator ParseOperator(uint flags) =>
        (DataValidationOperator)((flags >> 20) & 0x7u);

    private static DataValidationErrorStyle ParseErrorStyle(uint flags) => ((flags >> 4) & 0x7u) switch
    {
        1 => DataValidationErrorStyle.Warning,
        2 => DataValidationErrorStyle.Information,
        _ => DataValidationErrorStyle.Stop,
    };

    private static bool UsesOperator(DataValidationType type) => type is
        DataValidationType.Whole or
        DataValidationType.Decimal or
        DataValidationType.Date or
        DataValidationType.Time or
        DataValidationType.TextLength;

    private static string FormatRange(ExcelRange range) => range.TopLeft == range.BottomRight
        ? range.TopLeft.ToString()
        : $"{range.TopLeft}:{range.BottomRight}";

    private static string? NullIfEmpty(string value) => value.Length == 0 ? null : value;
}
