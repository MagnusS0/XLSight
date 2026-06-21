namespace XLSight.Query;

/// <summary>Represents a syntax or validation error in an XLSight Query DSL statement.</summary>
public sealed class QueryDslException : Exception
{
    /// <summary>Gets the zero-based character position in the query string where the error occurred, or -1 if unknown.</summary>
    public int Position { get; }

    /// <summary>Creates a new query DSL exception.</summary>
    /// <param name="message">The query diagnostic message.</param>
    /// <param name="position">The zero-based character position of the error, or -1 if unknown.</param>
    public QueryDslException(string message, int position = -1)
        : base(message)
    {
        Position = position;
    }

    /// <summary>Creates a new query DSL exception with an inner exception.</summary>
    /// <param name="message">The query diagnostic message.</param>
    /// <param name="innerException">The exception that caused this error.</param>
    public QueryDslException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
