using System.Buffers.Text;
using System.Net;
using System.Text;
using XLSight.Internal.Metadata;
using XLSight.Internal.Sinks;
using XLSight.Models;

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

        if (!Utf8Parser.TryParse(valueBytes, out double d, out _))
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

    private static string UnescapeXml(string value)
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
