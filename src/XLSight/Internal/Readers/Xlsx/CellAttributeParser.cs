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
        if (!TryParseCommonCellAttrs(attrBytes, out column, out row, out kind, out styleIndex, out isEmptyElement))
        {
            ParseCellAttrsGeneral(attrBytes, out column, out row, out kind, out styleIndex, out isEmptyElement);
        }
    }

    /// <summary>
    /// Fast path for the attribute shape Excel and common writers emit:
    /// <c>( ' ' ('r'|'s'|'t') '="' value '"' )* ( '&gt;' | '/&gt;' )</c>, with <c>r</c> as
    /// 1–3 uppercase letters plus 1–7 digits, <c>s</c> as 1–9 digits and <c>t</c> as a known
    /// type code. Returns <see langword="false"/> for anything else (other attributes, other
    /// whitespace, single quotes, prefixes, entities, unusual values) so the caller falls back
    /// to <see cref="ParseCellAttrsGeneral"/>. When it returns <see langword="true"/> the outputs
    /// equal the general parser's: the digit and letter limits keep the arithmetic exact, where
    /// <see cref="Utf8Parser"/> would otherwise decide on overflow, signs or trailing bytes.
    /// </summary>
    internal static bool TryParseCommonCellAttrs(
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
        isEmptyElement = false;

        int pos = 0;
        int len = attrBytes.Length;
        while (true)
        {
            if ((uint)pos >= (uint)len) { return false; }

            byte b = attrBytes[pos];
            if (b == (byte)'>')
            {
                return pos == len - 1;
            }

            if (b == (byte)'/')
            {
                isEmptyElement = true;
                return pos == len - 2 && attrBytes[pos + 1] == (byte)'>';
            }

            // Separator, one-letter name, '=' and '"', then at least one value byte and the
            // closing quote.
            if (b != (byte)' ' || pos + 5 >= len
                || attrBytes[pos + 2] != (byte)'=' || attrBytes[pos + 3] != (byte)'"')
            {
                return false;
            }

            byte name = attrBytes[pos + 1];
            pos += 4;
            bool ok = name switch
            {
                (byte)'r' => TryReadCellRef(attrBytes, ref pos, out column, out row),
                (byte)'s' => TryReadDigits(attrBytes, ref pos, maxDigits: 9, out styleIndex),
                (byte)'t' => TryReadKind(attrBytes, ref pos, out kind),
                _ => false,
            };

            if (!ok || pos >= len || attrBytes[pos] != (byte)'"') { return false; }
            pos++;
        }
    }

    // Column letters (1–3, uppercase) followed by row digits (1–7).
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool TryReadCellRef(ReadOnlySpan<byte> bytes, ref int pos, out int column, out int row)
    {
        int col = 0;
        int start = pos;
        while (pos < bytes.Length)
        {
            uint letter = (uint)(bytes[pos] - 'A');
            if (letter > 25) { break; }
            col = (col * 26) + (int)letter + 1;
            pos++;
        }

        column = col;
        row = 0;
        return pos - start is >= 1 and <= 3 && TryReadDigits(bytes, ref pos, maxDigits: 7, out row);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool TryReadDigits(ReadOnlySpan<byte> bytes, ref int pos, int maxDigits, out int value)
    {
        int v = 0;
        int start = pos;
        while (pos < bytes.Length)
        {
            uint digit = (uint)(bytes[pos] - '0');
            if (digit > 9) { break; }
            v = (v * 10) + (int)digit;
            pos++;
        }

        value = v;
        return pos - start >= 1 && pos - start <= maxDigits;
    }

    // Switches on the first byte; the caller guarantees two bytes are available at pos.
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool TryReadKind(ReadOnlySpan<byte> bytes, ref int pos, out CellDataKind kind)
    {
        kind = CellDataKind.Number;
        if (bytes[pos + 1] == (byte)'"')
        {
            switch (bytes[pos])
            {
                case (byte)'s': kind = CellDataKind.SharedString; break;
                case (byte)'n': break;
                case (byte)'b': kind = CellDataKind.Boolean; break;
                case (byte)'e': kind = CellDataKind.Error; break;
                case (byte)'d': kind = CellDataKind.FormulaString; break; // ISO date: see ParseKind.
                default: return false;
            }

            pos++;
            return true;
        }

        if (bytes[pos..].StartsWith("str\""u8))
        {
            kind = CellDataKind.FormulaString;
            pos += 3;
            return true;
        }

        if (bytes[pos..].StartsWith("inlineStr\""u8))
        {
            kind = CellDataKind.InlineString;
            pos += 9;
            return true;
        }

        return false;
    }

    /// <summary>General attribute tokenizer; handles every shape the fast path declines.</summary>
    internal static void ParseCellAttrsGeneral(
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

        // Single-pass forward tokenizer: one scan instead of three IndexOf passes.
        int pos = 0;
        int len = attrBytes.Length;

        while (pos < len)
        {
            byte ch = attrBytes[pos];
            if (ch is (byte)' ' or (byte)'\t' or (byte)'\r' or (byte)'\n') { pos++; continue; }
            if (ch is (byte)'>' or (byte)'/') { break; }

            int eq = attrBytes[pos..].IndexOf((byte)'=');
            if (eq < 0) { break; }

            // Only dispatch on single-char attr names (r, t, s); skip others (cm, ph, …).
            bool known = eq == 1 && (ch is (byte)'r' or (byte)'t' or (byte)'s');
            pos += eq + 1;
            if (pos >= len) { break; }

            byte quote = attrBytes[pos++];
            if (quote is not ((byte)'"' or (byte)'\'')) { break; }

            int closeQuote = attrBytes[pos..].IndexOf(quote);
            if (closeQuote < 0) { break; }

            if (known)
            {
                var value = attrBytes.Slice(pos, closeQuote);
                switch (ch)
                {
                    case (byte)'r': _ = ColumnRefDecoder.TryParse(value, out column, out row); break;
                    case (byte)'t': kind = ParseKind(value); break;
                    case (byte)'s': _ = Utf8Parser.TryParse(value, out styleIndex, out _); break;
                }
            }

            pos += closeQuote + 1;
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
