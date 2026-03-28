using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Runtime.InteropServices;

namespace XLSight.Models;

/// <summary>
/// Represents the decoded value of a single Excel cell.
/// Use the factory methods to create instances and typed accessors to retrieve values.
/// </summary>
// Fields ordered (double, string, enum) to eliminate internal padding: 8 + 8 + 4 + 4pad = 24 bytes, no gaps.
[StructLayout(LayoutKind.Sequential)]
public readonly struct ExcelCellValue : IEquatable<ExcelCellValue>
{
    private readonly double _numeric;   // number, bool (0.0/1.0), or DateTime ticks cast to double
    private readonly string? _text;     // text, error code, or formula text
    private readonly CellType _type;

    private ExcelCellValue(CellType type, double numeric, string? text)
    {
        _type = type;
        _numeric = numeric;
        _text = text;
    }

    /// <summary>Gets the type of data stored in this cell value.</summary>
    public CellType CellType => _type;

    /// <summary>Gets a value indicating whether this cell is empty (no value).</summary>
    public bool IsEmpty => _type == CellType.Empty;

    /// <summary>Gets a value indicating whether this cell has a non-empty value.</summary>
    public bool HasValue => _type != CellType.Empty;

    // ── Sentinel ──────────────────────────────────────────────────────────────

    // Static readonly fields on value types are zero-initialized by default — no explicit initializer needed.
    /// <summary>A sentinel representing an empty cell with no value.</summary>
    public static readonly ExcelCellValue Empty;

    // ── Factory methods ───────────────────────────────────────────────────────

    /// <summary>Creates a cell value holding a numeric (double) value.</summary>
    /// <param name="value">The numeric value.</param>
    /// <returns>A new <see cref="ExcelCellValue"/> of type <see cref="CellType.Number"/>.</returns>
    public static ExcelCellValue FromNumber(double value) =>
        new(CellType.Number, value, null);

    /// <summary>Creates a cell value holding a date/time value.</summary>
    /// <param name="value">The date/time value.</param>
    /// <returns>A new <see cref="ExcelCellValue"/> of type <see cref="CellType.Date"/>.</returns>
    public static ExcelCellValue FromDate(DateTime value) =>
        new(CellType.Date, (double)value.Ticks, null);

    /// <summary>Creates a cell value holding a text string.</summary>
    /// <param name="value">The text string.</param>
    /// <returns>A new <see cref="ExcelCellValue"/> of type <see cref="CellType.Text"/>.</returns>
    public static ExcelCellValue FromText(string value) =>
        new(CellType.Text, 0.0, value);

    /// <summary>Creates a cell value holding a boolean value.</summary>
    /// <param name="value">The boolean value.</param>
    /// <returns>A new <see cref="ExcelCellValue"/> of type <see cref="CellType.Boolean"/>.</returns>
    public static ExcelCellValue FromBoolean(bool value) =>
        new(CellType.Boolean, value ? 1.0 : 0.0, null);

    /// <summary>Creates a cell value holding an Excel error code.</summary>
    /// <param name="errorCode">The error code string, e.g. "#REF!".</param>
    /// <returns>A new <see cref="ExcelCellValue"/> of type <see cref="CellType.Error"/>.</returns>
    public static ExcelCellValue FromError(string errorCode) =>
        new(CellType.Error, 0.0, errorCode);

    /// <summary>Creates a cell value holding a formula string.</summary>
    /// <param name="formulaText">The raw formula text, e.g. "=SUM(A1:A10)".</param>
    /// <returns>A new <see cref="ExcelCellValue"/> of type <see cref="CellType.Formula"/>.</returns>
    public static ExcelCellValue FromFormula(string formulaText) =>
        new(CellType.Formula, 0.0, formulaText);

    // ── Typed accessors — throw InvalidOperationException on wrong type ────────

    /// <summary>Returns the numeric value. Throws if <see cref="CellType"/> is not <see cref="CellType.Number"/>.</summary>
    /// <returns>The double value stored in this cell.</returns>
    /// <exception cref="InvalidOperationException">Thrown when the cell type is not Number.</exception>
    public double AsNumber()
    {
        if (_type != CellType.Number)
        {
            ThrowWrongType(CellType.Number);
        }

        return _numeric;
    }

    /// <summary>Returns the date/time value. Throws if <see cref="CellType"/> is not <see cref="CellType.Date"/>.</summary>
    /// <returns>The <see cref="DateTime"/> stored in this cell.</returns>
    /// <exception cref="InvalidOperationException">Thrown when the cell type is not Date.</exception>
    public DateTime AsDate()
    {
        if (_type != CellType.Date)
        {
            ThrowWrongType(CellType.Date);
        }

        return new DateTime((long)_numeric);
    }

    /// <summary>Returns the text value. Throws if <see cref="CellType"/> is not <see cref="CellType.Text"/>.</summary>
    /// <returns>The string stored in this cell.</returns>
    /// <exception cref="InvalidOperationException">Thrown when the cell type is not Text.</exception>
    public string AsText()
    {
        if (_type != CellType.Text)
        {
            ThrowWrongType(CellType.Text);
        }

        return _text!;
    }

    /// <summary>Returns the boolean value. Throws if <see cref="CellType"/> is not <see cref="CellType.Boolean"/>.</summary>
    /// <returns>The bool stored in this cell.</returns>
    /// <exception cref="InvalidOperationException">Thrown when the cell type is not Boolean.</exception>
    public bool AsBoolean()
    {
        if (_type != CellType.Boolean)
        {
            ThrowWrongType(CellType.Boolean);
        }

        return _numeric != 0.0;
    }

    /// <summary>Returns the error code string. Throws if <see cref="CellType"/> is not <see cref="CellType.Error"/>.</summary>
    /// <returns>The error code string, e.g. "#REF!".</returns>
    /// <exception cref="InvalidOperationException">Thrown when the cell type is not Error.</exception>
    public string AsError()
    {
        if (_type != CellType.Error)
        {
            ThrowWrongType(CellType.Error);
        }

        return _text!;
    }

    /// <summary>Returns the formula text. Throws if <see cref="CellType"/> is not <see cref="CellType.Formula"/>.</summary>
    /// <returns>The raw formula string, e.g. "=SUM(A1:A10)".</returns>
    /// <exception cref="InvalidOperationException">Thrown when the cell type is not Formula.</exception>
    public string AsFormula()
    {
        if (_type != CellType.Formula)
        {
            ThrowWrongType(CellType.Formula);
        }

        return _text!;
    }

    // ── Try-pattern accessors — never throw ───────────────────────────────────

    /// <summary>Tries to get the numeric value without throwing.</summary>
    /// <param name="value">The numeric value if successful.</param>
    /// <returns>True if this cell holds a number; otherwise false.</returns>
    public bool TryGetNumber(out double value)
    {
        if (_type == CellType.Number)
        {
            value = _numeric;
            return true;
        }

        value = default;
        return false;
    }

    /// <summary>Tries to get the date/time value without throwing.</summary>
    /// <param name="value">The date/time value if successful.</param>
    /// <returns>True if this cell holds a date; otherwise false.</returns>
    public bool TryGetDate(out DateTime value)
    {
        if (_type == CellType.Date)
        {
            value = new DateTime((long)_numeric);
            return true;
        }

        value = default;
        return false;
    }

    /// <summary>Tries to get the text value without throwing.</summary>
    /// <param name="value">The text string if successful.</param>
    /// <returns>True if this cell holds text; otherwise false.</returns>
    public bool TryGetText([NotNullWhen(true)] out string? value)
    {
        if (_type == CellType.Text)
        {
            value = _text!;
            return true;
        }

        value = null;
        return false;
    }

    /// <summary>Tries to get the boolean value without throwing.</summary>
    /// <param name="value">The boolean value if successful.</param>
    /// <returns>True if this cell holds a boolean; otherwise false.</returns>
    public bool TryGetBoolean(out bool value)
    {
        if (_type == CellType.Boolean)
        {
            value = _numeric != 0.0;
            return true;
        }

        value = default;
        return false;
    }

    // ── Display ───────────────────────────────────────────────────────────────

    /// <summary>Returns a culture-invariant string representation of the cell value.</summary>
    /// <returns>A string representation suitable for display or debugging.</returns>
    public override string ToString() => _type switch
    {
        CellType.Empty   => "Empty",
        CellType.Number  => _numeric.ToString("G", CultureInfo.InvariantCulture),
        CellType.Date    => new DateTime((long)_numeric).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
        CellType.Text    => _text ?? string.Empty,
        CellType.Boolean => _numeric != 0.0 ? "TRUE" : "FALSE",
        CellType.Error   => $"#ERROR: {_text}",
        CellType.Formula => $"={_text}",
        _                     => string.Empty,
    };

    // ── Equality ──────────────────────────────────────────────────────────────

    /// <summary>Returns true if both cell values have the same type and value.</summary>
    /// <param name="other">The other cell value to compare.</param>
    /// <returns>True if equal; otherwise false.</returns>
    public bool Equals(ExcelCellValue other) =>
        _type == other._type &&
        _numeric == other._numeric &&
        string.Equals(_text, other._text, StringComparison.Ordinal);

    /// <inheritdoc/>
    public override bool Equals(object? obj) =>
        obj is ExcelCellValue other && Equals(other);

    /// <inheritdoc/>
    public override int GetHashCode() =>
        HashCode.Combine(_type, _numeric, _text);

    /// <summary>Returns true if <paramref name="left"/> equals <paramref name="right"/>.</summary>
    public static bool operator ==(ExcelCellValue left, ExcelCellValue right) => left.Equals(right);

    /// <summary>Returns true if <paramref name="left"/> does not equal <paramref name="right"/>.</summary>
    public static bool operator !=(ExcelCellValue left, ExcelCellValue right) => !left.Equals(right);

    // ── Helpers ───────────────────────────────────────────────────────────────

    private void ThrowWrongType(CellType expected) =>
        throw new InvalidOperationException(
            $"Cannot access cell value as {expected}; actual type is {_type}.");
}
