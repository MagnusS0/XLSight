using XLSight.Exceptions;
using XLSight.Models;

namespace XLSight.Parsing;

/// <summary>
/// Parses Excel cell references from worksheet XML attributes such as "BC123".
/// </summary>
internal static class CellReferenceParser
{
    /// <summary>
    /// Parses a single cell reference into an <see cref="ExcelAddress"/>.
    /// Throws <see cref="InvalidAddressException"/> if the input is invalid.
    /// </summary>
    public static ExcelAddress Parse(ReadOnlySpan<char> input)
    {
        if (!TryParse(input, out ExcelAddress address))
        {
            throw new InvalidAddressException(new string(input));
        }

        return address;
    }

    /// <summary>
    /// Tries to parse a single cell reference into an <see cref="ExcelAddress"/>.
    /// Returns <see langword="false"/> without throwing on invalid input.
    /// </summary>
    public static bool TryParse(ReadOnlySpan<char> input, out ExcelAddress address)
    {
        address = default;
        if (input.IsEmpty)
        {
            return false;
        }

        int splitIndex = 0;
        while (splitIndex < input.Length && input[splitIndex] >= 'A' && input[splitIndex] <= 'Z')
        {
            splitIndex++;
        }

        if (splitIndex is 0 || splitIndex == input.Length)
        {
            return false;
        }

        if (!AddressParser.TryParseColumn(input[..splitIndex], out int column) ||
            !TryParseRow(input[splitIndex..], out int row))
        {
            return false;
        }

        address = new ExcelAddress(column, row);
        return true;
    }

    private static bool TryParseRow(ReadOnlySpan<char> digits, out int row)
    {
        row = 0;
        if (digits.IsEmpty)
        {
            return false;
        }

        foreach (char c in digits)
        {
            if (c < '0' || c > '9')
            {
                return false;
            }

            if (row > ((ExcelLimits.MaxRows - (c - '0')) / 10))
            {
                return false;
            }

            row = row * 10 + (c - '0');
        }

        return row >= 1 && row <= ExcelLimits.MaxRows;
    }
}
