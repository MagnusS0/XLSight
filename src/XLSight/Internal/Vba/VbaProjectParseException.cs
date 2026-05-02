namespace XLSight.Internal.Vba;

/// <summary>Represents a structural failure while parsing a VBA project binary.</summary>
internal sealed class VbaProjectParseException : Exception
{
    public VbaProjectParseException(string message)
        : base(message)
    {
    }

    public VbaProjectParseException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
