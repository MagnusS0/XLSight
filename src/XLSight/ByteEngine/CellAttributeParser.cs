using System.Buffers.Text;
using XLSight.Worksheets;

namespace XLSight.ByteEngine;

/// <summary>
/// Extracts r=, t=, s= attributes from raw UTF-8 attribute bytes without string allocation.
/// </summary>
internal static class CellAttributeParser
{
    private static ReadOnlySpan<byte> RAttr => "r=\""u8;
    private static ReadOnlySpan<byte> TAttr => "t=\""u8;
    private static ReadOnlySpan<byte> SAttr => "s=\""u8;

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

        int idx = attrBytes.IndexOf(RAttr);
        if (idx >= 0)
        {
            var valueStart = attrBytes[(idx + 3)..];
            int end = valueStart.IndexOf((byte)'"');
            if (end > 0)
            {
                // Return value intentionally ignored: defaults (0, 0) are used on failure.
                _ = ColumnRefDecoder.TryParse(valueStart[..end], out column, out row);
            }
        }

        idx = attrBytes.IndexOf(TAttr);
        if (idx >= 0)
        {
            var valueStart = attrBytes[(idx + 3)..];
            int end = valueStart.IndexOf((byte)'"');
            if (end > 0)
            {
                kind = ParseKind(valueStart[..end]);
            }
        }

        idx = attrBytes.IndexOf(SAttr);
        if (idx >= 0)
        {
            var valueStart = attrBytes[(idx + 3)..];
            int end = valueStart.IndexOf((byte)'"');
            if (end > 0)
            {
                // Return value intentionally ignored: default styleIndex=0 is used on failure.
                _ = Utf8Parser.TryParse(valueStart[..end], out styleIndex, out _);
            }
        }
    }

    /// <summary>
    /// Parses the row index from a &lt;row ...&gt; tag's attribute bytes.
    /// The r= attribute on &lt;row&gt; is a plain integer, not a cell reference.
    /// </summary>
    internal static bool ParseRowIndex(ReadOnlySpan<byte> attrBytes, out int rowIndex)
    {
        rowIndex = 0;
        int idx = attrBytes.IndexOf(RAttr);
        if (idx < 0)
        {
            return false;
        }

        var valueStart = attrBytes[(idx + 3)..];
        int end = valueStart.IndexOf((byte)'"');
        if (end <= 0)
        {
            return false;
        }

        return Utf8Parser.TryParse(valueStart[..end], out rowIndex, out _);
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
