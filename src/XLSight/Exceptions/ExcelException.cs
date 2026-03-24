namespace XLSight;

/// <summary>Base exception for all XLSight errors.</summary>
public class ExcelException : Exception
{
    public ExcelException(string message) : base(message) { }

    public ExcelException(string message, Exception innerException) : base(message, innerException) { }
}
