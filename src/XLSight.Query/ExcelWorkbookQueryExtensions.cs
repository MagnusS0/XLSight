using XLSight.Analysis;

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
        ArgumentNullException.ThrowIfNull(workbook);
        ArgumentNullException.ThrowIfNull(queryText);
        ct.ThrowIfCancellationRequested();
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
        ArgumentNullException.ThrowIfNull(workbook);
        ArgumentNullException.ThrowIfNull(spec);
        ct.ThrowIfCancellationRequested();
        return ExecuteQueryCoreAsync(workbook, spec, ct);
    }

    private static async Task<QueryResult> ExecuteQueryCoreAsync(
        ExcelWorkbook workbook,
        SheetQuerySpec spec,
        CancellationToken ct)
    {
        SheetQuery query = await BuildSheetQueryAsync(workbook, spec, ct).ConfigureAwait(false);
        return await query.ExecuteAsync(ct).ConfigureAwait(false);
    }

    private static SheetQuery BuildSheetQuery(ExcelWorkbook workbook, SheetQuerySpec spec)
    {
        ArgumentNullException.ThrowIfNull(workbook);
        ArgumentNullException.ThrowIfNull(spec);
        ValidateSupportedHeader(spec);

        int headerRow;
        if (spec.Header.Kind == SheetQueryHeaderKind.Row)
        {
            headerRow = spec.Header.Row;
        }
        else if (spec.Header.Kind == SheetQueryHeaderKind.Auto)
        {
            SheetInfo info = workbook.AnalyzeSheet(spec.Sheet, AnalysisLevel.Full);
            headerRow = ResolveAutoHeaderRow(info, spec.Range);
        }
        else
        {
            headerRow = 0;
        }

        return ConfigureSheetQuery(workbook, spec, headerRow);
    }

    private static async Task<SheetQuery> BuildSheetQueryAsync(
        ExcelWorkbook workbook,
        SheetQuerySpec spec,
        CancellationToken ct)
    {
        ValidateSupportedHeader(spec);

        int headerRow;
        if (spec.Header.Kind == SheetQueryHeaderKind.Row)
        {
            headerRow = spec.Header.Row;
        }
        else if (spec.Header.Kind == SheetQueryHeaderKind.Auto)
        {
            SheetInfo info = await workbook
                .AnalyzeSheetAsync(spec.Sheet, AnalysisLevel.Full, options: null, ct)
                .ConfigureAwait(false);
            headerRow = ResolveAutoHeaderRow(info, spec.Range);
        }
        else
        {
            headerRow = 0;
        }

        return ConfigureSheetQuery(workbook, spec, headerRow);
    }

    private static void ValidateSupportedHeader(SheetQuerySpec spec)
    {
        if (spec.Header.Kind == SheetQueryHeaderKind.Column)
        {
            throw new NotSupportedException("HEADER COLUMN is reserved for transposed tables and is not supported by the row-oriented query engine.");
        }
    }

    private static SheetQuery ConfigureSheetQuery(ExcelWorkbook workbook, SheetQuerySpec spec, int headerRow)
    {
        SheetQuery query = workbook.QueryRange(spec.Sheet, spec.Range, headerRow);

        foreach (SheetQueryPredicate predicate in spec.Predicates)
        {
            query.WhereCell(predicate.Column, predicate.Op, predicate.Literal);
        }

        if (spec.GroupBy is { } groupBy)
        {
            query.GroupBy(groupBy);
        }

        if (spec.Aggregates.Count > 0)
        {
            query.Select([.. spec.Aggregates]);
        }

        if (spec.Limit is { } limit)
        {
            query.Take(limit);
        }

        return query;
    }

    /// <summary>
    /// Resolves the 1-based header row for a HEADER AUTO query by consulting inferred regions.
    /// Returns 0 when no confident match is found (falls back to first non-empty row behaviour).
    /// </summary>
    private static int ResolveAutoHeaderRow(SheetInfo info, ExcelRange range)
    {
        if (info.Inferred is not { } inferred)
        {
            return 0;
        }

        // Unbounded range: no column/row box to intersect, so use the sheet-level header.
        if (range.IsUnbounded)
        {
            return inferred.HeaderRowIndex;
        }

        // Bounded range: find the first data region overlapping the query box on BOTH axes,
        // so side-by-side tables sharing rows don't match a neighbour's header row.
        int queryTop = range.TopLeft.Row;
        int queryBottom = range.BottomRight.Row;
        int queryLeft = range.TopLeft.Column;
        int queryRight = range.BottomRight.Column;

        foreach (RegionInfo region in inferred.Regions)
        {
            if (region.Kind is not (RegionKind.DataTable or RegionKind.Crosstab)
                || region.HeaderRows.Count == 0)
            {
                continue;
            }

            bool rowsOverlap = region.Range.TopLeft.Row <= queryBottom
                && region.Range.BottomRight.Row >= queryTop;
            bool colsOverlap = region.Range.TopLeft.Column <= queryRight
                && region.Range.BottomRight.Column >= queryLeft;

            if (rowsOverlap && colsOverlap)
            {
                int headerRow = region.HeaderRows[0];

                // The region's header can sit above a sub-range that deliberately excludes it.
                // Returning an out-of-range header would make QueryRange throw, so fall back to
                // first-non-empty-row binding instead.
                return headerRow >= queryTop && headerRow <= queryBottom ? headerRow : 0;
            }
        }

        // No region intersects the bounded range: bind the first non-empty row in range.
        return 0;
    }

}
