using System.Diagnostics.CodeAnalysis;
using System.Globalization;

namespace XLSight.Models;

public readonly struct ExcelCellValue : IEquatable<ExcelCellValue>
{
    private readonly ExcelCellType _type;
    private readonly double _numeric;   // number, bool (0.0/1.0), or DateTime ticks cast to double
    private readonly string? _text;     // text, error code, or formula text

    private ExcelCellValue(ExcelCellType type, double numeric, string? text)
    {
        _type = type;
        _numeric = numeric;
        _text = text;
    }

    public ExcelCellType CellType => _type;
    public bool IsEmpty => _type == ExcelCellType.Empty;
    public bool HasValue => _type != ExcelCellType.Empty;

    // ── Sentinel ──────────────────────────────────────────────────────────────

    // Static readonly fields on value types are zero-initialized by default — no explicit initializer needed.
    public static readonly ExcelCellValue Empty;

    // ── Factory methods ───────────────────────────────────────────────────────

    public static ExcelCellValue FromNumber(double value) =>
        new(ExcelCellType.Number, value, null);

    public static ExcelCellValue FromDate(DateTime value) =>
        new(ExcelCellType.Date, (double)value.Ticks, null);

    public static ExcelCellValue FromText(string value) =>
        new(ExcelCellType.Text, 0.0, value);

    public static ExcelCellValue FromBoolean(bool value) =>
        new(ExcelCellType.Boolean, value ? 1.0 : 0.0, null);

    public static ExcelCellValue FromError(string errorCode) =>
        new(ExcelCellType.Error, 0.0, errorCode);

    public static ExcelCellValue FromFormula(string formulaText) =>
        new(ExcelCellType.Formula, 0.0, formulaText);

    // ── Typed accessors — throw InvalidOperationException on wrong type ────────

    public double AsNumber()
    {
        if (_type != ExcelCellType.Number)
        {
            ThrowWrongType(ExcelCellType.Number);
        }

        return _numeric;
    }

    public DateTime AsDate()
    {
        if (_type != ExcelCellType.Date)
        {
            ThrowWrongType(ExcelCellType.Date);
        }

        return new DateTime((long)_numeric);
    }

    public string AsText()
    {
        if (_type != ExcelCellType.Text)
        {
            ThrowWrongType(ExcelCellType.Text);
        }

        return _text!;
    }

    public bool AsBoolean()
    {
        if (_type != ExcelCellType.Boolean)
        {
            ThrowWrongType(ExcelCellType.Boolean);
        }

        return _numeric != 0.0;
    }

    public string AsError()
    {
        if (_type != ExcelCellType.Error)
        {
            ThrowWrongType(ExcelCellType.Error);
        }

        return _text!;
    }

    public string AsFormula()
    {
        if (_type != ExcelCellType.Formula)
        {
            ThrowWrongType(ExcelCellType.Formula);
        }

        return _text!;
    }

    // ── Try-pattern accessors — never throw ───────────────────────────────────

    public bool TryGetNumber(out double value)
    {
        if (_type == ExcelCellType.Number)
        {
            value = _numeric;
            return true;
        }

        value = default;
        return false;
    }

    public bool TryGetDate(out DateTime value)
    {
        if (_type == ExcelCellType.Date)
        {
            value = new DateTime((long)_numeric);
            return true;
        }

        value = default;
        return false;
    }

    public bool TryGetText([NotNullWhen(true)] out string? value)
    {
        if (_type == ExcelCellType.Text)
        {
            value = _text!;
            return true;
        }

        value = null;
        return false;
    }

    public bool TryGetBoolean(out bool value)
    {
        if (_type == ExcelCellType.Boolean)
        {
            value = _numeric != 0.0;
            return true;
        }

        value = default;
        return false;
    }

    // ── Display ───────────────────────────────────────────────────────────────

    public override string ToString() => _type switch
    {
        ExcelCellType.Empty   => "Empty",
        ExcelCellType.Number  => _numeric.ToString("G", CultureInfo.InvariantCulture),
        ExcelCellType.Date    => new DateTime((long)_numeric).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
        ExcelCellType.Text    => _text ?? string.Empty,
        ExcelCellType.Boolean => _numeric != 0.0 ? "TRUE" : "FALSE",
        ExcelCellType.Error   => $"#ERROR: {_text}",
        ExcelCellType.Formula => $"={_text}",
        _                     => string.Empty,
    };

    // ── Equality ──────────────────────────────────────────────────────────────

    public bool Equals(ExcelCellValue other) =>
        _type == other._type &&
        _numeric == other._numeric &&
        string.Equals(_text, other._text, StringComparison.Ordinal);

    public override bool Equals(object? obj) =>
        obj is ExcelCellValue other && Equals(other);

    public override int GetHashCode() =>
        HashCode.Combine(_type, _numeric, _text);

    public static bool operator ==(ExcelCellValue left, ExcelCellValue right) => left.Equals(right);
    public static bool operator !=(ExcelCellValue left, ExcelCellValue right) => !left.Equals(right);

    // ── Helpers ───────────────────────────────────────────────────────────────

    private void ThrowWrongType(ExcelCellType expected) =>
        throw new InvalidOperationException(
            $"Cannot access cell value as {expected}; actual type is {_type}.");
}
