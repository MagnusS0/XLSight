namespace XLSight.Query;

/// <summary>The materialized result of an executed query.</summary>
public sealed class QueryResult
{
    /// <summary>
    /// Gets the result column names: the source column names for row results, or the group-by
    /// column followed by aggregate labels for aggregate results. Empty when the query was
    /// pruned via column statistics without opening the sheet.
    /// </summary>
    public required IReadOnlyList<string> Columns { get; init; }

    /// <summary>Gets the result rows. One row per group for grouped queries, a single row for global aggregates.</summary>
    public required IReadOnlyList<QueryResultRow> Rows { get; init; }

    /// <summary>Gets the number of non-empty data rows scanned (rows after the header row).</summary>
    public required int RowsScanned { get; init; }

    /// <summary>Gets the number of scanned rows that matched all filters.</summary>
    public required int RowsMatched { get; init; }

    /// <summary>
    /// Gets per-column counts of cells that could not be coerced to an aggregate's input type
    /// and were skipped, with sample row indices for provenance. Empty when nothing was skipped.
    /// </summary>
    public required IReadOnlyList<UnaggregatableColumn> Unaggregatable { get; init; }
}
