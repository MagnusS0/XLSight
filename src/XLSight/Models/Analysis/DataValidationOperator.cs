namespace XLSight.Analysis;

/// <summary>Specifies the comparison operator used by a data-validation rule.</summary>
public enum DataValidationOperator : byte
{
    /// <summary>Between two bounds.</summary>
    Between = 0,
    /// <summary>Outside two bounds.</summary>
    NotBetween = 1,
    /// <summary>Equal to the bound.</summary>
    Equal = 2,
    /// <summary>Not equal to the bound.</summary>
    NotEqual = 3,
    /// <summary>Greater than the bound.</summary>
    GreaterThan = 4,
    /// <summary>Less than the bound.</summary>
    LessThan = 5,
    /// <summary>Greater than or equal to the bound.</summary>
    GreaterThanOrEqual = 6,
    /// <summary>Less than or equal to the bound.</summary>
    LessThanOrEqual = 7,
}
