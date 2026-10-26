using System.Buffers.Text;
using System.Net;
using System.Text;
using XLSight.Internal.Metadata;
using XLSight.Internal.Sinks;

namespace XLSight.Internal.Readers.Xlsx;

/// <summary>
/// Decodes a cell value from raw UTF-8 bytes into an <see cref="ExcelCellValue"/>.
/// Uses <see cref="Utf8Parser"/> for the hot path (numbers, SST indices, booleans)
/// to avoid string allocation and locale-sensitive parsing.
/// </summary>
internal static class Utf8CellDecoder
{
    /// <summary>Decodes <paramref name="valueBytes"/> according to <paramref name="kind"/>.</summary>
    internal static ExcelCellValue Decode(
        ReadOnlySpan<byte> valueBytes,
        CellDataKind kind,
        int styleIndex,
        SharedStringTable sharedStrings,
        StyleTable styles,
        bool isDate1904)
    {
        if (valueBytes.IsEmpty)
        {
            return ExcelCellValue.Empty;
        }

        switch (kind)
        {
            case CellDataKind.Number:
                {
                    return DecodeNumber(valueBytes, styleIndex, styles, isDate1904);
                }

            case CellDataKind.SharedString:
                {
                    if (!Utf8Parser.TryParse(valueBytes, out int idx, out _))
                    {
                        return ExcelCellValue.Empty;
                    }

                    return ExcelCellValue.FromSharedString(sharedStrings.GetString(idx), idx);
                }

            case CellDataKind.Boolean:
                {
                    return ExcelCellValue.FromBoolean(valueBytes[0] == (byte)'1');
                }

            case CellDataKind.Error:
                {
                    return ExcelCellValue.FromError(Encoding.UTF8.GetString(valueBytes));
                }

            case CellDataKind.FormulaString:
            case CellDataKind.InlineString:
                {
                    // Slow path: allocate string and unescape XML entities via WebUtility.HtmlDecode.
                    return ExcelCellValue.FromText(UnescapeXml(Encoding.UTF8.GetString(valueBytes)));
                }

            default:
                {
                    return ExcelCellValue.Empty;
                }
        }
    }

    private static ExcelCellValue DecodeNumber(
        ReadOnlySpan<byte> valueBytes,
        int styleIndex,
        StyleTable styles,
        bool isDate1904)
    {
        // Trim ASCII whitespace — Utf8Parser requires exact format, no leading/trailing spaces.
        while (!valueBytes.IsEmpty && valueBytes[0] <= 32) { valueBytes = valueBytes[1..]; }
        while (!valueBytes.IsEmpty && valueBytes[^1] <= 32) { valueBytes = valueBytes[..^1]; }

        if (!TryParseShortDecimal(valueBytes, out double d)
            && !Utf8Parser.TryParse(valueBytes, out d, out _))
        {
            return ExcelCellValue.Empty;
        }

        var cls = styles.GetClassification(styleIndex);
        if (cls is FormatClass.Date or FormatClass.DateTime or FormatClass.Time)
        {
            var dt = ExcelDateConverter.FromSerial(d, isDate1904);
            return dt.HasValue ? ExcelCellValue.FromDate(dt.Value) : ExcelCellValue.Empty;
        }

        return ExcelCellValue.FromNumber(d);
    }

    // Exact powers of ten: every 10^k with k <= 22 is representable without rounding.
    private static ReadOnlySpan<double> ExactPowersOfTen =>
    [
        1e0, 1e1, 1e2, 1e3, 1e4, 1e5, 1e6, 1e7, 1e8, 1e9, 1e10, 1e11,
        1e12, 1e13, 1e14, 1e15, 1e16, 1e17, 1e18, 1e19, 1e20, 1e21, 1e22,
    ];

    /// <summary>
    /// Fast path for the plain decimals Excel writes most often (<c>-?\d+(\.\d+)?</c>, at most
    /// 15 significant-or-leading digits). The digits form an integer below 2^53 and the divisor
    /// is an exact power of ten, so one IEEE division yields the correctly rounded result —
    /// bit-identical to <see cref="Utf8Parser"/> (Clinger's fast path). Anything else
    /// (exponents, longer mantissas, trailing bytes) returns <see langword="false"/> so the
    /// caller falls back to <see cref="Utf8Parser"/>.
    /// </summary>
    internal static bool TryParseShortDecimal(ReadOnlySpan<byte> s, out double value)
    {
        value = 0;
        int i = 0;
        bool negative = false;
        if (s.Length > 0 && s[0] == (byte)'-')
        {
            negative = true;
            i = 1;
        }

        int intStart = i;
        ulong mantissa = 0;
        while (i < s.Length)
        {
            uint digit = (uint)(s[i] - '0');
            if (digit > 9) { break; }
            mantissa = (mantissa * 10) + digit;
            i++;
        }

        int intDigits = i - intStart;
        if (intDigits == 0 || intDigits > 15)
        {
            return false;
        }

        int fracDigits = 0;
        if (i < s.Length)
        {
            if (s[i] != (byte)'.')
            {
                return false;
            }

            i++;
            int fracStart = i;
            while (i < s.Length)
            {
                uint digit = (uint)(s[i] - '0');
                if (digit > 9) { return false; }
                mantissa = (mantissa * 10) + digit;
                i++;
                if (i - fracStart + intDigits > 15) { return false; }
            }

            fracDigits = i - fracStart;
            if (fracDigits == 0)
            {
                return false;
            }
        }

        double result = fracDigits == 0 ? mantissa : mantissa / ExactPowersOfTen[fracDigits];
        value = negative ? -result : result;
        return true;
    }

    internal static string UnescapeXml(string value)
    {
        if (!value.Contains('&', StringComparison.Ordinal))
        {
            return value;
        }

        // WebUtility.HtmlDecode handles named entities (&amp; &lt; &gt; &apos; &quot;)
        // and numeric character references (&#9; &#xA; etc.) — XmlReader decodes these
        // automatically, so the byte engine must match.
        return WebUtility.HtmlDecode(value);
    }
}
