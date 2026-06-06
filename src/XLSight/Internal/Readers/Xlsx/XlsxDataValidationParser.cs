using System.Text;
using XLSight.Analysis;
using XLSight.Internal.Parsing;
using static XLSight.Internal.Readers.Xlsx.XmlByteReader;

namespace XLSight.Internal.Readers.Xlsx;

internal static class XlsxDataValidationParser
{
    private static ReadOnlySpan<byte> TagFormula1 => "formula1"u8;
    private static ReadOnlySpan<byte> TagFormula2 => "formula2"u8;
    private static ReadOnlySpan<byte> TagFormula => "f"u8;
    private static ReadOnlySpan<byte> TagSequenceOfReferences => "sqref"u8;

    internal static DataValidationBuilder ParseAttributes(ReadOnlySpan<byte> attributes)
    {
        var builder = new DataValidationBuilder();
        var remaining = attributes;

        while (!remaining.IsEmpty)
        {
            int eqPos = remaining.IndexOf((byte)'=');
            if (eqPos < 0) { break; }

            ReadOnlySpan<byte> namePart = remaining[..eqPos];
            int nameEnd = namePart.Length - 1;
            while (nameEnd >= 0 && IsXmlWhitespace(namePart[nameEnd])) { nameEnd--; }
            int nameStart = nameEnd;
            while (nameStart > 0 && !IsXmlWhitespace(namePart[nameStart - 1])) { nameStart--; }
            ReadOnlySpan<byte> name = namePart[nameStart..(nameEnd + 1)];

            int afterEq = eqPos + 1;
            if ((uint)afterEq >= (uint)remaining.Length) { break; }

            byte quote = remaining[afterEq];
            if (quote is not ((byte)'"' or (byte)'\''))
            {
                remaining = remaining[afterEq..];
                continue;
            }

            var valueSpan = remaining[(afterEq + 1)..];
            int valueEnd = valueSpan.IndexOf(quote);
            if (valueEnd < 0) { break; }

            ApplyAttribute(ref builder, name, valueSpan[..valueEnd]);
            remaining = valueSpan[(valueEnd + 1)..];
        }

        return builder;
    }

    private static bool IsXmlWhitespace(byte b) => b is (byte)' ' or (byte)'\t' or (byte)'\r' or (byte)'\n';

    private static void ApplyAttribute(ref DataValidationBuilder builder, ReadOnlySpan<byte> name, ReadOnlySpan<byte> value)
    {
        if (name.SequenceEqual("type"u8))
        {
            builder.Type = ParseType(value);
        }
        else if (name.SequenceEqual("sqref"u8))
        {
            builder.SequenceOfReferences = Decode(value);
        }
        else if (name.SequenceEqual("operator"u8))
        {
            builder.Operator = ParseOperator(value);
        }
        else if (name.SequenceEqual("allowBlank"u8))
        {
            builder.AllowBlank = ParseBoolean(value);
        }
        else if (name.SequenceEqual("showDropDown"u8))
        {
            builder.ShowDropDown = ParseBoolean(value);
        }
        else if (name.SequenceEqual("showInputMessage"u8))
        {
            builder.ShowInputMessage = ParseBoolean(value);
        }
        else if (name.SequenceEqual("showErrorMessage"u8))
        {
            builder.ShowErrorMessage = ParseBoolean(value);
        }
        else if (name.SequenceEqual("errorStyle"u8))
        {
            builder.ErrorStyle = ParseErrorStyle(value);
        }
        else if (name.SequenceEqual("errorTitle"u8))
        {
            builder.ErrorTitle = DecodeNullable(value);
        }
        else if (name.SequenceEqual("error"u8))
        {
            builder.ErrorMessage = DecodeNullable(value);
        }
        else if (name.SequenceEqual("promptTitle"u8))
        {
            builder.PromptTitle = DecodeNullable(value);
        }
        else if (name.SequenceEqual("prompt"u8))
        {
            builder.PromptMessage = DecodeNullable(value);
        }
    }

    private static bool ParseBoolean(ReadOnlySpan<byte> value) =>
        value.SequenceEqual("1"u8) || System.Text.Ascii.EqualsIgnoreCase(value, "true"u8);

    internal static DataValidationInfo? Complete(DataValidationBuilder builder, ReadOnlySpan<byte> body)
    {
        string sequenceOfReferences = builder.SequenceOfReferences.Trim();
        if (sequenceOfReferences.Length == 0)
        {
            sequenceOfReferences = Decode(ReadElementText(body, TagSequenceOfReferences)).Trim();
        }

        if (sequenceOfReferences.Length == 0)
        {
            return null;
        }

        return new DataValidationInfo
        {
            Type = builder.Type,
            SequenceOfReferences = sequenceOfReferences,
            Ranges = ParseRanges(sequenceOfReferences),
            Formula1 = DecodeNullable(ReadFormula(body, TagFormula1))?.Trim(),
            Formula2 = DecodeNullable(ReadFormula(body, TagFormula2))?.Trim(),
            Operator = builder.Operator,
            AllowBlank = builder.AllowBlank,
            ShowDropDown = builder.ShowDropDown,
            ShowInputMessage = builder.ShowInputMessage,
            ShowErrorMessage = builder.ShowErrorMessage,
            ErrorStyle = builder.ErrorStyle,
            ErrorTitle = builder.ErrorTitle,
            ErrorMessage = builder.ErrorMessage,
            PromptTitle = builder.PromptTitle,
            PromptMessage = builder.PromptMessage,
        };
    }

    private static ReadOnlySpan<byte> ReadFormula(ReadOnlySpan<byte> body, ReadOnlySpan<byte> tag)
    {
        ReadOnlySpan<byte> value = ReadElementText(body, tag);
        ReadOnlySpan<byte> nested = ReadElementText(value, TagFormula);
        return nested.IsEmpty ? value : nested;
    }

    private static ReadOnlySpan<byte> ReadElementText(ReadOnlySpan<byte> body, ReadOnlySpan<byte> tag)
    {
        if (TryFindStartTag(body, tag, out StartTagMatch match, out _) != TagSearchResult.Found || match.IsEmptyElement)
        {
            return [];
        }

        ReadOnlySpan<byte> content = body[match.EndExclusive..];
        return TryFindEndTag(content, tag, out int closeIndex, out _, out _) == TagSearchResult.Found
            ? content[..closeIndex]
            : [];
    }

    private static List<ExcelRange> ParseRanges(string sequenceOfReferences)
    {
        var ranges = new List<ExcelRange>();
        ReadOnlySpan<char> remaining = sequenceOfReferences.AsSpan();
        while (!remaining.IsEmpty)
        {
            remaining = remaining.TrimStart();
            int separator = remaining.IndexOfAny(" \t\r\n");
            ReadOnlySpan<char> reference = separator < 0 ? remaining : remaining[..separator];
            if (!reference.IsEmpty && AddressParser.TryParse(reference, out ExcelRange range))
            {
                ranges.Add(range);
            }

            if (separator < 0)
            {
                break;
            }

            remaining = remaining[(separator + 1)..];
        }

        return ranges;
    }

    private static DataValidationType ParseType(ReadOnlySpan<byte> value) => value switch
    {
        _ when value.SequenceEqual("whole"u8) => DataValidationType.Whole,
        _ when value.SequenceEqual("decimal"u8) => DataValidationType.Decimal,
        _ when value.SequenceEqual("list"u8) => DataValidationType.List,
        _ when value.SequenceEqual("date"u8) => DataValidationType.Date,
        _ when value.SequenceEqual("time"u8) => DataValidationType.Time,
        _ when value.SequenceEqual("textLength"u8) => DataValidationType.TextLength,
        _ when value.SequenceEqual("custom"u8) => DataValidationType.Custom,
        _ => DataValidationType.None,
    };

    private static DataValidationOperator? ParseOperator(ReadOnlySpan<byte> value) => value switch
    {
        _ when value.SequenceEqual("between"u8) => DataValidationOperator.Between,
        _ when value.SequenceEqual("notBetween"u8) => DataValidationOperator.NotBetween,
        _ when value.SequenceEqual("equal"u8) => DataValidationOperator.Equal,
        _ when value.SequenceEqual("notEqual"u8) => DataValidationOperator.NotEqual,
        _ when value.SequenceEqual("greaterThan"u8) => DataValidationOperator.GreaterThan,
        _ when value.SequenceEqual("lessThan"u8) => DataValidationOperator.LessThan,
        _ when value.SequenceEqual("greaterThanOrEqual"u8) => DataValidationOperator.GreaterThanOrEqual,
        _ when value.SequenceEqual("lessThanOrEqual"u8) => DataValidationOperator.LessThanOrEqual,
        _ => null,
    };

    private static DataValidationErrorStyle ParseErrorStyle(ReadOnlySpan<byte> value)
    {
        if (value.SequenceEqual("warning"u8))
        {
            return DataValidationErrorStyle.Warning;
        }

        return value.SequenceEqual("information"u8)
            ? DataValidationErrorStyle.Information
            : DataValidationErrorStyle.Stop;
    }

    private static string Decode(ReadOnlySpan<byte> value) =>
        value.IsEmpty ? string.Empty : Utf8CellDecoder.UnescapeXml(Encoding.UTF8.GetString(value));

    private static string? DecodeNullable(ReadOnlySpan<byte> value)
    {
        string decoded = Decode(value);
        return decoded.Length == 0 ? null : decoded;
    }

    internal struct DataValidationBuilder
    {
        internal DataValidationType Type;
        internal string SequenceOfReferences;
        internal DataValidationOperator? Operator;
        internal bool AllowBlank;
        internal bool ShowDropDown;
        internal bool ShowInputMessage;
        internal bool ShowErrorMessage;
        internal DataValidationErrorStyle ErrorStyle;
        internal string? ErrorTitle;
        internal string? ErrorMessage;
        internal string? PromptTitle;
        internal string? PromptMessage;
    }
}
