using System.Runtime.InteropServices;

namespace XLSight.Models;

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

    /// <summary>Constructs a bounded rectangular range.</summary>
    public ExcelRange(ExcelAddress topLeft, ExcelAddress bottomRight)
    {
        TopLeft = topLeft;
        BottomRight = bottomRight;
        _isUnbounded = false;
    }

    private ExcelRange(bool isUnbounded)
    {
        _isUnbounded = isUnbounded;
        TopLeft = default;
        BottomRight = default;
    }

    /// <summary>Number of columns in this range.</summary>
    public int Width => BottomRight.Column - TopLeft.Column + 1;

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
