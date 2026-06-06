using System.Net;
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
        return new DataValidationBuilder
        {
            Type = ParseType(ReadAttribute(attributes, "type="u8)),
            SequenceOfReferences = Decode(ReadAttribute(attributes, "sqref="u8)),
            Operator = ParseOperator(ReadAttribute(attributes, "operator="u8)),
            AllowBlank = ReadBoolean(attributes, "allowBlank="u8),
            ShowDropDown = ReadBoolean(attributes, "showDropDown="u8),
            ShowInputMessage = ReadBoolean(attributes, "showInputMessage="u8),
            ShowErrorMessage = ReadBoolean(attributes, "showErrorMessage="u8),
            ErrorStyle = ParseErrorStyle(ReadAttribute(attributes, "errorStyle="u8)),
            ErrorTitle = DecodeNullable(ReadAttribute(attributes, "errorTitle="u8)),
            ErrorMessage = DecodeNullable(ReadAttribute(attributes, "error="u8)),
            PromptTitle = DecodeNullable(ReadAttribute(attributes, "promptTitle="u8)),
            PromptMessage = DecodeNullable(ReadAttribute(attributes, "prompt="u8)),
        };
    }

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

    private static ReadOnlySpan<byte> ReadAttribute(ReadOnlySpan<byte> attributes, ReadOnlySpan<byte> name) =>
        CellAttributeParser.TryGetAttributeValue(attributes, name, out ReadOnlySpan<byte> value) ? value : [];

    private static bool ReadBoolean(ReadOnlySpan<byte> attributes, ReadOnlySpan<byte> name)
    {
        ReadOnlySpan<byte> value = ReadAttribute(attributes, name);
        return value.SequenceEqual("1"u8) || AsciiEqualsIgnoreCase(value, "true"u8);
    }

    private static bool AsciiEqualsIgnoreCase(ReadOnlySpan<byte> left, ReadOnlySpan<byte> right)
    {
        if (left.Length != right.Length)
        {
            return false;
        }

        for (int i = 0; i < left.Length; i++)
        {
            byte leftValue = left[i];
            byte rightValue = right[i];
            if (leftValue is >= (byte)'A' and <= (byte)'Z')
            {
                leftValue += (byte)('a' - 'A');
            }

            if (leftValue != rightValue)
            {
                return false;
            }
        }

        return true;
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

    private static string Decode(ReadOnlySpan<byte> value)
    {
        if (value.IsEmpty)
        {
            return string.Empty;
        }

        string text = Encoding.UTF8.GetString(value);
        return text.Contains('&', StringComparison.Ordinal) ? WebUtility.HtmlDecode(text) : text;
    }

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
