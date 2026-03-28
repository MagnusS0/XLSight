namespace XLSight.Models;

/// <summary>Controls whether cell values or formula strings are returned by the reader.</summary>
public enum ReadMode : byte
{
    /// <summary>Return decoded cached values (dates, numbers, text, booleans, errors). Default.</summary>
    Values = 0,

    /// <summary>
    /// If a cell has a formula, return <see cref="CellType.Formula"/> with the raw formula
    /// text; otherwise return the normal decoded value.
    /// </summary>
    Formulas = 1,
}
