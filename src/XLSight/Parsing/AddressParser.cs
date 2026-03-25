using XLSight.Models;

namespace XLSight.Parsing;

/// <summary>
/// Span-based parser for Excel address and range strings.
/// Handles single cells (A1), full ranges (A1:C50), column ranges (A:C), and row ranges (1:10).
/// </summary>
internal static class AddressParser
{
    /// <summary>
    /// Parses an Excel address string into an <see cref="ExcelRange"/>.
    /// Throws <see cref="InvalidAddressException"/> if the input is invalid.
    /// </summary>
    public static ExcelRange Parse(ReadOnlySpan<char> input)
    {
        if (!TryParse(input, out ExcelRange range))
        {
            throw new InvalidAddressException(new string(input));
        }

        return range;
    }

    /// <summary>
    /// Tries to parse an Excel address string into an <see cref="ExcelRange"/>.
    /// Returns false without throwing on invalid input.
    /// </summary>
    public static bool TryParse(ReadOnlySpan<char> input, out ExcelRange range)
    {
        range = default;
        if (input.IsEmpty)
        {
            return false;
        }

        int colonPos = input.IndexOf(':');
        if (colonPos < 0)
        {
            if (!CellReferenceParser.TryParse(input, out ExcelAddress address))
            {
                return false;
            }

            range = new ExcelRange(address, address);
            return true;
        }

        ReadOnlySpan<char> left = input[..colonPos];
        ReadOnlySpan<char> right = input[(colonPos + 1)..];
        return TryParseColonSeparatedRange(left, right, out range);
    }

    private static bool TryParseColonSeparatedRange(
        ReadOnlySpan<char> left,
        ReadOnlySpan<char> right,
        out ExcelRange range)
    {
        range = default;
        if (left.IsEmpty && right.IsEmpty)
        {
            range = ExcelRange.Unbounded;
            return true;
        }

        if (left.IsEmpty || right.IsEmpty || right.Contains(':'))
        {
            return false;
        }

        if (IsAllUpperLetters(left) && IsAllUpperLetters(right))
        {
            return TryParseColumnRange(left, right, out range);
        }

        if (IsAllDigits(left) && IsAllDigits(right))
        {
            return TryParseRowRange(left, right, out range);
        }

        return TryParseRectangularRange(left, right, out range);
    }

    private static bool TryParseColumnRange(
        ReadOnlySpan<char> left,
        ReadOnlySpan<char> right,
        out ExcelRange range)
    {
        range = default;
        if (!TryParseColumn(left, out int startColumn) ||
            !TryParseColumn(right, out int endColumn) ||
            startColumn > endColumn)
        {
            return false;
        }

        range = new ExcelRange(
            new ExcelAddress(startColumn, 1),
            new ExcelAddress(endColumn, ExcelLimits.MaxRows));
        return true;
    }

    private static bool TryParseRowRange(
        ReadOnlySpan<char> left,
        ReadOnlySpan<char> right,
        out ExcelRange range)
    {
        range = default;
        if (!TryParseRow(left, out int startRow) ||
            !TryParseRow(right, out int endRow) ||
            startRow > endRow)
        {
            return false;
        }

        range = new ExcelRange(
            new ExcelAddress(1, startRow),
            new ExcelAddress(ExcelLimits.MaxColumns, endRow));
        return true;
    }

    private static bool TryParseRectangularRange(
        ReadOnlySpan<char> left,
        ReadOnlySpan<char> right,
        out ExcelRange range)
    {
        range = default;
        if (!CellReferenceParser.TryParse(left, out ExcelAddress topLeft) ||
            !CellReferenceParser.TryParse(right, out ExcelAddress bottomRight) ||
            topLeft.Column > bottomRight.Column ||
            topLeft.Row > bottomRight.Row)
        {
            return false;
        }

        range = new ExcelRange(topLeft, bottomRight);
        return true;
    }

    /// <summary>
    /// Converts column letters (e.g. "XFD") to a 1-based column index using bijective base-26.
    /// </summary>
    internal static bool TryParseColumn(ReadOnlySpan<char> letters, out int column)
    {
        column = 0;
        if (letters.IsEmpty || letters.Length > 3)
        {
            return false;
        }

        foreach (char c in letters)
        {
            if (c < 'A' || c > 'Z')
            {
                column = 0;
                return false;
            }

            column = column * 26 + (c - 'A' + 1);
        }

        if (column is < 1 or > ExcelLimits.MaxColumns)
        {
            column = 0;
            return false;
        }

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

    private static bool IsAllUpperLetters(ReadOnlySpan<char> span)
    {
        if (span.IsEmpty)
        {
            return false;
        }

        foreach (char c in span)
        {
            if (c < 'A' || c > 'Z')
            {
                return false;
            }
        }

        return true;
    }

    private static bool IsAllDigits(ReadOnlySpan<char> span)
    {
        if (span.IsEmpty)
        {
            return false;
        }

        foreach (char c in span)
        {
            if (c < '0' || c > '9')
            {
                return false;
            }
        }

        return true;
    }
}
