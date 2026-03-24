using System.Runtime.InteropServices;

namespace XLSight.Models;

/// <summary>
/// Represents a single Excel cell address as a 1-based (column, row) pair.
/// Column 1 = A, Column 26 = Z, Column 27 = AA, Column 16384 = XFD.
/// </summary>
[StructLayout(LayoutKind.Auto)]
public readonly record struct ExcelAddress(int Column, int Row)
{
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
