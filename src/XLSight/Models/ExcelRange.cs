using System.Runtime.InteropServices;
using XLSight.Internal.Parsing;

namespace XLSight;

/// <summary>
/// Represents a rectangular Excel range. Use <see cref="Unbounded"/> as a sentinel
/// for ranges that span all rows or columns.
/// </summary>
[StructLayout(LayoutKind.Auto)]
public readonly record struct ExcelRange
{
    /// <summary>The top-left cell of the range.</summary>
    public ExcelAddress TopLeft { get; }

    /// <summary>
    /// The bottom-right cell of the range. Undefined for the <see cref="Unbounded"/> sentinel.
    /// </summary>
    public ExcelAddress BottomRight { get; }

    private readonly bool _isUnbounded;

    /// <summary>True when this range is the unbounded sentinel.</summary>
    public bool IsUnbounded => _isUnbounded;

    /// <summary>
    /// Sentinel representing a fully unbounded range (e.g. entire sheet or full column).
    /// <see cref="Contains"/> always returns true. <see cref="Height"/> throws.
    /// </summary>
    public static readonly ExcelRange Unbounded = new(isUnbounded: true);

    /// <summary>Constructs a bounded rectangular range, normalizing so TopLeft &lt;= BottomRight.</summary>
    public ExcelRange(ExcelAddress topLeft, ExcelAddress bottomRight)
    {
        int minCol = Math.Min(topLeft.Column, bottomRight.Column);
        int minRow = Math.Min(topLeft.Row, bottomRight.Row);
        int maxCol = Math.Max(topLeft.Column, bottomRight.Column);
        int maxRow = Math.Max(topLeft.Row, bottomRight.Row);
        TopLeft = new ExcelAddress(minCol, minRow);
        BottomRight = new ExcelAddress(maxCol, maxRow);
        _isUnbounded = false;
    }

    private ExcelRange(bool isUnbounded)
    {
        _isUnbounded = isUnbounded;
        TopLeft = default;
        BottomRight = default;
    }

    /// <summary>
    /// Parses an Excel range address string (e.g. "A1:D10") into an <see cref="ExcelRange"/>.
    /// The input is normalized to uppercase before parsing.
    /// </summary>
    /// <param name="range">The range address string to parse.</param>
    /// <returns>The parsed <see cref="ExcelRange"/>.</returns>
    /// <exception cref="InvalidAddressException">Thrown when the address cannot be parsed.</exception>
    public static ExcelRange Parse(string range)
    {
        ArgumentNullException.ThrowIfNull(range);
        return AddressParser.Parse(range.ToUpperInvariant().AsSpan());
    }

    /// <summary>
    /// Tries to parse an Excel range address string (e.g. "A1:D10") into an <see cref="ExcelRange"/>.
    /// The input is normalized to uppercase before parsing.
    /// </summary>
    /// <param name="range">The range address string to parse.</param>
    /// <param name="result">When successful, the parsed <see cref="ExcelRange"/>.</param>
    /// <returns>True if parsing succeeded; otherwise false.</returns>
    public static bool TryParse(string range, out ExcelRange result)
    {
        if (range is null) { result = default; return false; }
        return AddressParser.TryParse(range.ToUpperInvariant().AsSpan(), out result);
    }

    /// <summary>Number of columns in this range.</summary>
    public int Width
    {
        get
        {
            if (_isUnbounded)
            {
                throw new InvalidOperationException("Range is unbounded — Width is not defined.");
            }
            return BottomRight.Column - TopLeft.Column + 1;
        }
    }

    /// <summary>
    /// Number of rows in this range.
    /// </summary>
    /// <exception cref="InvalidOperationException">Thrown when <see cref="IsUnbounded"/> is true.</exception>
    public int Height
    {
        get
        {
            if (_isUnbounded)
            {
                throw new InvalidOperationException("Range is unbounded — Height is not defined.");
            }

            return BottomRight.Row - TopLeft.Row + 1;
        }
    }

    /// <summary>
    /// Returns true if <paramref name="address"/> falls within this range.
    /// Always returns true when <see cref="IsUnbounded"/> is true.
    /// </summary>
    public bool Contains(ExcelAddress address)
    {
        if (_isUnbounded)
        {
            return true;
        }

        return address.Column >= TopLeft.Column
            && address.Column <= BottomRight.Column
            && address.Row >= TopLeft.Row
            && address.Row <= BottomRight.Row;
    }
}
