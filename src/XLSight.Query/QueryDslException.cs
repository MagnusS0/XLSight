namespace XLSight.Query;

/// <summary>Represents a syntax or validation error in an XLSight Query DSL statement.</summary>
public sealed class QueryDslException : Exception
{
    /// <summary>Creates a new query DSL exception.</summary>
    /// <param name="message">The query diagnostic message.</param>
    public QueryDslException(string message)
        : base(message)
    {
    }

    /// <summary>Creates a new query DSL exception with an inner exception.</summary>
    /// <param name="message">The query diagnostic message.</param>
    /// <param name="innerException">The exception that caused this error.</param>
    public QueryDslException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
