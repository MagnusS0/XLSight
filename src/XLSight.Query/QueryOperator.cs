namespace XLSight.Query;

/// <summary>Comparison operators for query filters.</summary>
public enum QueryOperator
{
    /// <summary>The cell value equals the literal.</summary>
    Equals,

    /// <summary>The cell value does not equal the literal (only cells of the literal's type can match).</summary>
    NotEquals,

    /// <summary>The cell value is less than the literal.</summary>
    LessThan,

    /// <summary>The cell value is less than or equal to the literal.</summary>
    LessThanOrEqual,

    /// <summary>The cell value is greater than the literal.</summary>
    GreaterThan,

    /// <summary>The cell value is greater than or equal to the literal.</summary>
    GreaterThanOrEqual,
}
