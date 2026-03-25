namespace XLSight.Exceptions;

/// <summary>Thrown when the file is not a valid xlsx or the XML is corrupt.</summary>
public sealed class MalformedWorkbookException : ExcelException
{
    public MalformedWorkbookException(string message) : base(message) { }

    public MalformedWorkbookException(string message, Exception innerException)
        : base(message, innerException) { }
}
