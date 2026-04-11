using System.Runtime.InteropServices;
using XLSight.Internal.Parsing;

namespace XLSight;

/// <summary>
/// Represents a single Excel cell address as a 1-based (column, row) pair.
/// Column 1 = A, Column 26 = Z, Column 27 = AA, Column 16384 = XFD.
/// </summary>
/// <param name="Column">The 1-based column index (1 = A, 16384 = XFD).</param>
/// <param name="Row">The 1-based row index (1 to 1048576).</param>
[StructLayout(LayoutKind.Auto)]
public readonly record struct ExcelAddress(int Column, int Row)
{
    /// <summary>
    /// Parses an Excel cell address string (e.g. "A1") into an <see cref="ExcelAddress"/>.
    /// The input is normalized to uppercase before parsing.
    /// </summary>
    /// <param name="address">The cell address string to parse.</param>
    /// <returns>The parsed <see cref="ExcelAddress"/>.</returns>
    /// <exception cref="InvalidAddressException">Thrown when the address cannot be parsed.</exception>
    public static ExcelAddress Parse(string address)
    {
        ArgumentNullException.ThrowIfNull(address);
        var span = address.ToUpperInvariant().AsSpan();
        if (!AddressParser.TryParseCell(span, out var result))
        {
            throw new InvalidAddressException(address);
        }
        return result;
    }

    /// <summary>
    /// Tries to parse an Excel cell address string (e.g. "A1") into an <see cref="ExcelAddress"/>.
    /// The input is normalized to uppercase before parsing.
    /// </summary>
    /// <param name="address">The cell address string to parse.</param>
    /// <param name="result">When successful, the parsed <see cref="ExcelAddress"/>.</param>
    /// <returns>True if parsing succeeded; otherwise false.</returns>
    public static bool TryParse(string address, out ExcelAddress result)
    {
        if (address is null) { result = default; return false; }
        var span = address.ToUpperInvariant().AsSpan();
        return AddressParser.TryParseCell(span, out result);
    }
    /// <summary>
    /// Returns the Excel-style address string, e.g. "A1", "BC42", "XFD1048576".
    /// </summary>
    public override string ToString()
    {
        return $"{ColumnIndexToLetters(Column)}{Row}";
    }

    /// <summary>
    /// Converts a 1-based column index to Excel column letters using bijective base-26.
    /// 1 → "A", 26 → "Z", 27 → "AA", 702 → "ZZ", 703 → "AAA", 16384 → "XFD".
    /// </summary>
    private static string ColumnIndexToLetters(int column)
    {
        // Build digits right-to-left in a stack-allocated buffer (max 3 chars: XFD).
        // Bijective base-26: subtract 1 before each modulus to shift A=1..Z=26 → A=0..Z=25.
        Span<char> buffer = stackalloc char[3];
        int position = 0;
        int remaining = column;

        while (remaining > 0)
        {
            remaining--;
            buffer[position++] = (char)('A' + (remaining % 26));
            remaining /= 26;
        }

        for (int i = 0, j = position - 1; i < j; i++, j--)
        {
            (buffer[i], buffer[j]) = (buffer[j], buffer[i]);
        }

        return new string(buffer[..position]);
    }
}
