namespace XLSight.Query;

/// <summary>Query entry points over <see cref="ExcelWorkbook"/>.</summary>
public static class ExcelWorkbookQueryExtensions
{
    /// <summary>
    /// Starts a streaming query over a sheet range. Column names are taken from the header row
    /// (by default the first non-empty row of the range), then filtered, grouped, and aggregated
    /// in a single pass with bounded memory.
    /// </summary>
    /// <param name="workbook">The open workbook.</param>
    /// <param name="sheet">The sheet name.</param>
    /// <param name="range">The range address, e.g. "A6:F2410". Case-insensitive.</param>
    /// <param name="headerRow">
    /// The 1-based sheet row containing the column headers, or 0 to use the first
    /// non-empty row of the range. Data rows start after the header row.
    /// </param>
    /// <returns>A query builder.</returns>
    /// <exception cref="ArgumentNullException">Thrown when an argument is null.</exception>
    /// <exception cref="InvalidAddressException">Thrown when the range cannot be parsed.</exception>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="headerRow"/> is negative or outside the range.</exception>
    public static SheetQuery QueryRange(this ExcelWorkbook workbook, string sheet, string range, int headerRow = 0)
    {
        ArgumentNullException.ThrowIfNull(range);
        return QueryRange(workbook, sheet, ExcelRange.Parse(range), headerRow);
    }

    /// <summary>
    /// Starts a streaming query over a typed sheet range. Column names are taken from the header
    /// row (by default the first non-empty row of the range), then filtered, grouped, and
    /// aggregated in a single pass with bounded memory.
    /// </summary>
    /// <param name="workbook">The open workbook.</param>
    /// <param name="sheet">The sheet name.</param>
    /// <param name="range">The range to query.</param>
    /// <param name="headerRow">
    /// The 1-based sheet row containing the column headers, or 0 to use the first
    /// non-empty row of the range. Data rows start after the header row.
    /// </param>
    /// <returns>A query builder.</returns>
    /// <exception cref="ArgumentNullException">Thrown when an argument is null.</exception>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="headerRow"/> is negative or outside the range.</exception>
    public static SheetQuery QueryRange(this ExcelWorkbook workbook, string sheet, ExcelRange range, int headerRow = 0)
    {
        ArgumentNullException.ThrowIfNull(workbook);
        ArgumentNullException.ThrowIfNull(sheet);
        ArgumentOutOfRangeException.ThrowIfNegative(headerRow);
        if (headerRow > 0 && !range.IsUnbounded
            && (headerRow < range.TopLeft.Row || headerRow > range.BottomRight.Row))
        {
            throw new ArgumentOutOfRangeException(
                nameof(headerRow), headerRow, $"Header row must lie within the queried range rows {range.TopLeft.Row}-{range.BottomRight.Row}.");
        }

        return new SheetQuery(workbook, sheet, range, headerRow);
    }

    /// <summary>Parses and executes an XLSight Query DSL statement.</summary>
    /// <param name="workbook">The open workbook.</param>
    /// <param name="queryText">The Query DSL text.</param>
    /// <returns>The materialized query result.</returns>
    /// <exception cref="QueryDslException">Thrown when the query text is invalid or unsupported.</exception>
    /// <exception cref="NotSupportedException">Thrown when the query uses reserved syntax that the engine cannot execute.</exception>
    public static QueryResult ExecuteQuery(this ExcelWorkbook workbook, string queryText)
    {
        ArgumentNullException.ThrowIfNull(queryText);
        return ExecuteQuery(workbook, SheetQuerySpec.Parse(queryText));
    }

    /// <summary>Executes a parsed XLSight Query DSL specification.</summary>
    /// <param name="workbook">The open workbook.</param>
    /// <param name="spec">The parsed query specification.</param>
    /// <returns>The materialized query result.</returns>
    /// <exception cref="NotSupportedException">Thrown when the query uses reserved syntax that the engine cannot execute.</exception>
    public static QueryResult ExecuteQuery(this ExcelWorkbook workbook, SheetQuerySpec spec)
    {
        SheetQuery query = BuildSheetQuery(workbook, spec);
        return query.Execute();
    }

    /// <summary>Parses and executes an XLSight Query DSL statement asynchronously.</summary>
    /// <param name="workbook">The open workbook.</param>
    /// <param name="queryText">The Query DSL text.</param>
    /// <param name="ct">A cancellation token.</param>
    /// <returns>A task that returns the materialized query result.</returns>
    /// <exception cref="QueryDslException">Thrown when the query text is invalid or unsupported.</exception>
    /// <exception cref="NotSupportedException">Thrown when the query uses reserved syntax that the engine cannot execute.</exception>
    public static Task<QueryResult> ExecuteQueryAsync(
        this ExcelWorkbook workbook,
        string queryText,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(queryText);
        return ExecuteQueryAsync(workbook, SheetQuerySpec.Parse(queryText), ct);
    }

    /// <summary>Executes a parsed XLSight Query DSL specification asynchronously.</summary>
    /// <param name="workbook">The open workbook.</param>
    /// <param name="spec">The parsed query specification.</param>
    /// <param name="ct">A cancellation token.</param>
    /// <returns>A task that returns the materialized query result.</returns>
    /// <exception cref="NotSupportedException">Thrown when the query uses reserved syntax that the engine cannot execute.</exception>
    public static Task<QueryResult> ExecuteQueryAsync(
        this ExcelWorkbook workbook,
        SheetQuerySpec spec,
        CancellationToken ct = default)
    {
        SheetQuery query = BuildSheetQuery(workbook, spec);
        return query.ExecuteAsync(ct);
    }

    private static SheetQuery BuildSheetQuery(ExcelWorkbook workbook, SheetQuerySpec spec)
    {
        ArgumentNullException.ThrowIfNull(workbook);
        ArgumentNullException.ThrowIfNull(spec);

        if (spec.Header.Kind == SheetQueryHeaderKind.Column)
        {
            throw new NotSupportedException("HEADER COLUMN is reserved for transposed tables and is not supported by the row-oriented query engine.");
        }

        int headerRow = spec.Header.Kind == SheetQueryHeaderKind.Row ? spec.Header.Row : 0;
        SheetQuery query = workbook.QueryRange(spec.Sheet, spec.Range, headerRow);

        foreach (SheetQueryPredicate predicate in spec.Predicates)
        {
            AddPredicate(query, predicate);
        }

        if (spec.GroupBy is { } groupBy)
        {
            query.GroupBy(groupBy);
        }

        if (spec.Aggregates.Count > 0)
        {
            query.Aggregate([.. spec.Aggregates]);
        }

        if (spec.Limit is { } limit)
        {
            query.Limit(limit);
        }

        return query;
    }

    private static void AddPredicate(SheetQuery query, SheetQueryPredicate predicate)
    {
        switch (predicate.Literal.CellType)
        {
            case CellType.Text:
                query.Where(predicate.Column, predicate.Op, predicate.Literal.AsText());
                break;
            case CellType.Number:
                query.Where(predicate.Column, predicate.Op, predicate.Literal.AsNumber());
                break;
            case CellType.Date:
                query.Where(predicate.Column, predicate.Op, predicate.Literal.AsDate());
                break;
            case CellType.Boolean:
                query.Where(predicate.Column, predicate.Op, predicate.Literal.AsBoolean());
                break;
            default:
                throw new QueryDslException($"Unsupported predicate literal type '{predicate.Literal.CellType}'.");
        }
    }
}
