namespace XLSight.Query;

/// <summary>A single result row.</summary>
public sealed class QueryResultRow
{
    /// <summary>Gets the 1-based sheet row index for row results, or null for aggregate results.</summary>
    public int? SourceRowIndex { get; init; }

    /// <summary>Gets the cell values, aligned with <see cref="QueryResult.Columns"/>.</summary>
    public required IReadOnlyList<ExcelCellValue> Values { get; init; }
}
