using System.Buffers.Text;
using System.Runtime.CompilerServices;
using XLSight.Internal.Sinks;

namespace XLSight.Internal.Readers.Xlsx;

/// <summary>
/// Extracts r=, t=, s= attributes from raw UTF-8 attribute bytes without string allocation.
/// </summary>
internal static class CellAttributeParser
{
    private static ReadOnlySpan<byte> RAttr => "r="u8;
    private static ReadOnlySpan<byte> TAttr => "t="u8;
    private static ReadOnlySpan<byte> SAttr => "s="u8;

    /// <summary>
    /// Parses attributes from a &lt;c ...&gt; or &lt;c .../&gt; tag.
    /// </summary>
    /// <param name="attrBytes">
    /// Bytes starting immediately after the tag name (after "c"),
    /// up to and including the closing "&gt;" or "/&gt;".
    /// </param>
    /// <param name="column">1-based column, or 0 if r= is absent.</param>
    /// <param name="row">1-based row from cell ref, or 0 if r= is absent.</param>
    /// <param name="kind">Cell data kind derived from t=.</param>
    /// <param name="styleIndex">Style index from s=, or 0 if absent.</param>
    /// <param name="isEmptyElement">True if the tag ends with "/&gt;".</param>
    internal static void ParseCellAttrs(
        ReadOnlySpan<byte> attrBytes,
        out int column,
        out int row,
        out CellDataKind kind,
        out int styleIndex,
        out bool isEmptyElement)
    {
        column = 0;
        row = 0;
        kind = CellDataKind.Number;
        styleIndex = 0;
        isEmptyElement = attrBytes.Length >= 2
            && attrBytes[^2] == (byte)'/'
            && attrBytes[^1] == (byte)'>';

        if (TryGetAttributeValue(attrBytes, RAttr, out var rValue))
        {
            _ = ColumnRefDecoder.TryParse(rValue, out column, out row);
        }

        if (TryGetAttributeValue(attrBytes, TAttr, out var tValue))
        {
            kind = ParseKind(tValue);
        }

        if (TryGetAttributeValue(attrBytes, SAttr, out var sValue))
        {
            _ = Utf8Parser.TryParse(sValue, out styleIndex, out _);
        }
    }

    /// <summary>
    /// Parses the row index from a &lt;row ...&gt; tag's attribute bytes.
    /// The r= attribute on &lt;row&gt; is a plain integer, not a cell reference.
    /// </summary>
    internal static bool ParseRowIndex(ReadOnlySpan<byte> attrBytes, out int rowIndex)
    {
        rowIndex = 0;
        if (!TryGetAttributeValue(attrBytes, RAttr, out var rValue))
        {
            return false;
        }

        return Utf8Parser.TryParse(rValue, out rowIndex, out _);
    }

    private static ReadOnlySpan<byte> RefAttr => "ref="u8;

    /// <summary>
    /// Extracts the <c>ref="..."</c> attribute value from a tag's attribute bytes.
    /// Used for <c>&lt;dimension&gt;</c> and <c>&lt;mergeCell&gt;</c> elements.
    /// </summary>
    internal static bool TryGetRefAttribute(ReadOnlySpan<byte> attrBytes, out ReadOnlySpan<byte> refValue)
        => TryGetAttributeValue(attrBytes, RefAttr, out refValue);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static bool TryGetAttributeValue(
        ReadOnlySpan<byte> attrBytes,
        ReadOnlySpan<byte> attributeName,
        out ReadOnlySpan<byte> value)
    {
        value = default;
        int idx = attrBytes.IndexOf(attributeName);
        if (idx < 0)
        {
            return false;
        }

        int quoteIndex = idx + attributeName.Length;
        if ((uint)quoteIndex >= (uint)attrBytes.Length)
        {
            return false;
        }

        byte quote = attrBytes[quoteIndex];
        if (quote is not ((byte)'"' or (byte)'\''))
        {
            return false;
        }

        var valueStart = attrBytes[(quoteIndex + 1)..];
        int end = valueStart.IndexOf(quote);
        if (end <= 0)
        {
            return false;
        }

        value = valueStart[..end];
        return true;
    }

    private static CellDataKind ParseKind(ReadOnlySpan<byte> tValue)
    {
        if (tValue.SequenceEqual("s"u8)) { return CellDataKind.SharedString; }
        if (tValue.SequenceEqual("b"u8)) { return CellDataKind.Boolean; }
        if (tValue.SequenceEqual("inlineStr"u8)) { return CellDataKind.InlineString; }
        if (tValue.SequenceEqual("str"u8)) { return CellDataKind.FormulaString; }
        if (tValue.SequenceEqual("e"u8)) { return CellDataKind.Error; }

        // "d" (ISO 8601 date string): treated as FormulaString for Phase 1.
        if (tValue.SequenceEqual("d"u8)) { return CellDataKind.FormulaString; }

        // "n" or anything else: numeric (same as absent t=).
        return CellDataKind.Number;
    }
}
