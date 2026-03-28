namespace XLSight.Models;

/// <summary>Identifies the data type of an Excel cell value.</summary>
public enum CellType : byte
{
    /// <summary>The cell is blank or has no value.</summary>
    Empty = 0,

    /// <summary>The cell contains a text string.</summary>
    Text = 1,

    /// <summary>The cell contains a numeric value.</summary>
    Number = 2,

    /// <summary>The cell contains a date or datetime value.</summary>
    Date = 3,

    /// <summary>The cell contains a boolean (TRUE/FALSE) value.</summary>
    Boolean = 4,

    /// <summary>The cell contains an Excel error such as #REF! or #VALUE!.</summary>
    Error = 5,

    /// <summary>The cell contains a formula string; only populated in <see cref="ReadMode.Formulas"/>.</summary>
    Formula = 6,
}
