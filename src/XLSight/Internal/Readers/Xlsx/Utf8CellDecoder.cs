using System.Buffers;
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
                    return ExcelCellValue.FromError(DecodeErrorCode(valueBytes));
                }

            case CellDataKind.FormulaString:
            case CellDataKind.InlineString:
                {
                    return ExcelCellValue.FromText(DecodeXmlText(valueBytes));
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

    /// <summary>
    /// Decodes XML character data to a string with a single allocation. Handles the five XML
    /// named entities and numeric references directly on UTF-8 bytes; any other '&amp;' form
    /// falls back to the historical <see cref="WebUtility.HtmlDecode(string)"/> path so output
    /// stays identical for unusual or malformed input.
    /// </summary>
    internal static string DecodeXmlText(ReadOnlySpan<byte> bytes)
    {
        int amp = bytes.IndexOf((byte)'&');
        if (amp < 0) { return bytes.IsEmpty ? string.Empty : Encoding.UTF8.GetString(bytes); }

        // Every recognised entity decodes to fewer bytes than its encoded form.
        const int StackLimit = 256;
        byte[]? rented = null;
        Span<byte> scratch = bytes.Length <= StackLimit
            ? stackalloc byte[StackLimit]
            : (rented = ArrayPool<byte>.Shared.Rent(bytes.Length));
        try
        {
            if (TryDecodeEntities(bytes, amp, scratch, out int written))
            {
                return Encoding.UTF8.GetString(scratch[..written]);
            }

            return UnescapeXml(Encoding.UTF8.GetString(bytes));
        }
        finally
        {
            if (rented is not null) { ArrayPool<byte>.Shared.Return(rented); }
        }
    }

    private static bool TryDecodeEntities(ReadOnlySpan<byte> src, int amp, Span<byte> dst, out int written)
    {
        src[..amp].CopyTo(dst);
        written = amp;
        int pos = amp;
        while (pos < src.Length)
        {
            ReadOnlySpan<byte> rest = src[pos..];
            int next = rest.IndexOf((byte)'&');
            if (next < 0)
            {
                rest.CopyTo(dst[written..]);
                written += rest.Length;
                return true;
            }

            rest[..next].CopyTo(dst[written..]);
            written += next;
            pos += next;

            ReadOnlySpan<byte> entity = src[(pos + 1)..];
            int semi = entity.Length > 12 ? entity[..12].IndexOf((byte)';') : entity.IndexOf((byte)';');
            if (semi <= 0 || !TryDecodeEntity(entity[..semi], dst[written..], out int produced))
            {
                return false;
            }

            written += produced;
            pos += semi + 2;
        }

        return true;
    }

    private static bool TryDecodeEntity(ReadOnlySpan<byte> body, Span<byte> dst, out int produced)
    {
        produced = 1;
        if (body.SequenceEqual("amp"u8)) { dst[0] = (byte)'&'; return true; }
        if (body.SequenceEqual("lt"u8)) { dst[0] = (byte)'<'; return true; }
        if (body.SequenceEqual("gt"u8)) { dst[0] = (byte)'>'; return true; }
        if (body.SequenceEqual("quot"u8)) { dst[0] = (byte)'"'; return true; }
        if (body.SequenceEqual("apos"u8)) { dst[0] = (byte)'\''; return true; }

        produced = 0;
        if (body.Length < 2 || body[0] != (byte)'#') { return false; }

        bool hex = body[1] is (byte)'x' or (byte)'X';
        ReadOnlySpan<byte> digits = body[(hex ? 2 : 1)..];
        if (digits.IsEmpty || digits.Length > 7) { return false; }

        int codePoint = 0;
        foreach (byte b in digits)
        {
            int d = b - '0';
            if ((uint)d > 9)
            {
                if (!hex) { return false; }
                d = (b | 0x20) - 'a' + 10;
                if (d is < 10 or > 15) { return false; }
            }

            codePoint = (codePoint * (hex ? 16 : 10)) + d;
        }

        // Only scalar values >= U+0001 take the fast path; NUL, surrogates and out-of-range
        // values keep the historical HtmlDecode behaviour.
        if (codePoint == 0 || !Rune.TryCreate(codePoint, out Rune rune)) { return false; }

        produced = rune.EncodeToUtf8(dst);
        return true;
    }

    private static readonly string[] s_errorCodes =
    [
        "#NULL!", "#DIV/0!", "#VALUE!", "#REF!", "#NAME?", "#NUM!", "#N/A", "#GETTING_DATA",
        "#SPILL!", "#CALC!", "#FIELD!", "#BLOCKED!", "#CONNECT!", "#BUSY!", "#UNKNOWN!",
    ];

    /// <summary>Returns a shared instance for the fixed set of Excel error codes.</summary>
    internal static string DecodeErrorCode(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length is > 0 and <= 16 && bytes[0] == (byte)'#')
        {
            foreach (string code in s_errorCodes)
            {
                if (code.Length == bytes.Length && Ascii.Equals(bytes, code)) { return code; }
            }
        }

        return Encoding.UTF8.GetString(bytes);
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
