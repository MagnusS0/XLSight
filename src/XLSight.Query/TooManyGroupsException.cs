namespace XLSight.Query;

/// <summary>
/// Thrown when a query produces more groups (or distinct values) than the configured cap.
/// Narrow the range, add filters, or raise the cap via <see cref="SheetQuery.WithGroupLimit"/>;
/// for genuinely high-cardinality workloads use an external engine instead.
/// </summary>
public sealed class TooManyGroupsException : ExcelException
{
    /// <summary>Initializes a new instance with the specified message.</summary>
    /// <param name="message">The error message.</param>
    public TooManyGroupsException(string message) : base(message) { }
}
