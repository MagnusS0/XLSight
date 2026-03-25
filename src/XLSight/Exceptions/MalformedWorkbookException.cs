namespace XLSight.Exceptions;

/// <summary>Thrown when the file is not a valid xlsx or the XML is corrupt.</summary>
public sealed class MalformedWorkbookException : ExcelException
{
    /// <summary>Initializes a new instance with the specified message.</summary>
    /// <param name="message">A description of what part of the workbook is malformed.</param>
    public MalformedWorkbookException(string message) : base(message) { }

    /// <summary>Initializes a new instance with the specified message and inner exception.</summary>
    /// <param name="message">A description of what part of the workbook is malformed.</param>
    /// <param name="innerException">The exception that caused this exception.</param>
    public MalformedWorkbookException(string message, Exception innerException)
        : base(message, innerException) { }
}
