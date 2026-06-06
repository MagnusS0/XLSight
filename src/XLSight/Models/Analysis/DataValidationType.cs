using System.Diagnostics.CodeAnalysis;

namespace XLSight.Analysis;

/// <summary>Specifies the kind of constraint applied by a worksheet data-validation rule.</summary>
public enum DataValidationType : byte
{
    /// <summary>Allows any value.</summary>
    None = 0,
    /// <summary>Allows whole numbers satisfying the rule.</summary>
    Whole = 1,
    /// <summary>Allows decimal numbers satisfying the rule.</summary>
    [SuppressMessage("Naming", "CA1720:Identifier contains type name", Justification = "Matches the OOXML data-validation type name.")]
    Decimal = 2,
    /// <summary>Allows values from a list.</summary>
    List = 3,
    /// <summary>Allows dates satisfying the rule.</summary>
    Date = 4,
    /// <summary>Allows times satisfying the rule.</summary>
    Time = 5,
    /// <summary>Allows text whose length satisfies the rule.</summary>
    TextLength = 6,
    /// <summary>Uses a custom formula to validate values.</summary>
    Custom = 7,
}
