namespace XLSight.Analysis;

/// <summary>Specifies the error-alert style used by a data-validation rule.</summary>
public enum DataValidationErrorStyle : byte
{
    /// <summary>Displays a stop alert.</summary>
    Stop = 0,
    /// <summary>Displays a warning alert.</summary>
    Warning = 1,
    /// <summary>Displays an information alert.</summary>
    Information = 2,
}
