namespace XLSight.Query;

/// <summary>Factory methods for aggregate projections used with <see cref="SheetQuery.Select"/>.</summary>
public static class QueryAggregates
{
    /// <summary>Sums the numeric cells of <paramref name="column"/>.</summary>
    /// <param name="column">The source column name.</param>
    public static AggregateSpec Sum(string column)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(column);
        return new(AggregateKind.Sum, column);
    }

    /// <summary>Counts the rows matching the query filters.</summary>
    public static AggregateSpec Count() => new(AggregateKind.Count, null);

    /// <summary>Takes the minimum of the numeric or date cells of <paramref name="column"/>.</summary>
    /// <param name="column">The source column name.</param>
    public static AggregateSpec Min(string column)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(column);
        return new(AggregateKind.Min, column);
    }

    /// <summary>Takes the maximum of the numeric or date cells of <paramref name="column"/>.</summary>
    /// <param name="column">The source column name.</param>
    public static AggregateSpec Max(string column)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(column);
        return new(AggregateKind.Max, column);
    }

    /// <summary>Averages the numeric cells of <paramref name="column"/>.</summary>
    /// <param name="column">The source column name.</param>
    public static AggregateSpec Average(string column)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(column);
        return new(AggregateKind.Average, column);
    }
}
