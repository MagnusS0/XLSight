namespace XLSight.Exceptions;

/// <summary>Base exception for all XLSight errors.</summary>
public class ExcelException : Exception
{
    /// <summary>Initializes a new instance with the specified message.</summary>
    /// <param name="message">The error message.</param>
    public ExcelException(string message) : base(message) { }

    /// <summary>Initializes a new instance with the specified message and inner exception.</summary>
    /// <param name="message">The error message.</param>
    /// <param name="innerException">The exception that caused this exception.</param>
    public ExcelException(string message, Exception innerException) : base(message, innerException) { }
}
