using XLSight.Analysis;
using XLSight.Query.Internal;

namespace XLSight.Query;

/// <summary>
/// A single-pass streaming query over a sheet range: filters, an optional group-by,
/// and aggregates are fused into one scan over borrowed rows, so memory scales with
/// group cardinality rather than row count. Build with
/// <see cref="ExcelWorkbookQueryExtensions.QueryRange(ExcelWorkbook, string, string, int)"/>.
/// Not thread-safe; execute at most one terminal per instance at a time.
/// </summary>
public sealed class SheetQuery
{
    private const int DefaultGroupLimit = 10_000;

    private readonly ExcelWorkbook _workbook;
    private readonly string _sheet;
    private readonly ExcelRange _range;
    private readonly int _headerRow;
    private readonly List<FilterSpec> _filters = [];
    private readonly List<AggregateSpec> _aggregates = [];
    private string? _groupBy;
    private int _limit = -1;
    private int _maxGroups = DefaultGroupLimit;
    private IReadOnlyList<ColumnProfile>? _stats;

    internal SheetQuery(ExcelWorkbook workbook, string sheet, ExcelRange range, int headerRow)
    {
        _workbook = workbook;
        _sheet = sheet;
        _range = range;
        _headerRow = headerRow;
    }

    // ── Filters ───────────────────────────────────────────────────────────────

    /// <summary>Adds a text filter. Filters are AND-combined; comparisons are ordinal.</summary>
    /// <param name="column">The column name (from the header row).</param>
    /// <param name="op">The comparison operator.</param>
    /// <param name="value">The text literal. Only text cells can match.</param>
    public SheetQuery Where(string column, QueryOperator op, string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(column);
        ArgumentNullException.ThrowIfNull(value);
        return AddFilter(column, op, ExcelCellValue.FromText(value));
    }

    /// <summary>Adds a numeric filter. Filters are AND-combined.</summary>
    /// <param name="column">The column name (from the header row).</param>
    /// <param name="op">The comparison operator.</param>
    /// <param name="value">The numeric literal. Only numeric cells can match.</param>
    public SheetQuery Where(string column, QueryOperator op, double value)
        => AddFilter(column, op, ExcelCellValue.FromNumber(value));

    /// <summary>Adds a date filter. Filters are AND-combined.</summary>
    /// <param name="column">The column name (from the header row).</param>
    /// <param name="op">The comparison operator.</param>
    /// <param name="value">The date literal. Only date cells can match.</param>
    public SheetQuery Where(string column, QueryOperator op, DateTime value)
        => AddFilter(column, op, ExcelCellValue.FromDate(value));

    /// <summary>Adds a boolean filter. Filters are AND-combined.</summary>
    /// <param name="column">The column name (from the header row).</param>
    /// <param name="op">Either <see cref="QueryOperator.Equals"/> or <see cref="QueryOperator.NotEquals"/>.</param>
    /// <param name="value">The boolean literal. Only boolean cells can match.</param>
    /// <exception cref="ArgumentException">Thrown when <paramref name="op"/> is an ordering operator.</exception>
    public SheetQuery Where(string column, QueryOperator op, bool value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(column);
        if (op is not (QueryOperator.Equals or QueryOperator.NotEquals))
        {
            throw new ArgumentException("Boolean filters support Equals and NotEquals only.", nameof(op));
        }

        return AddFilter(column, op, ExcelCellValue.FromBoolean(value));
    }

    // ── Shaping ───────────────────────────────────────────────────────────────

    /// <summary>Groups aggregate results by the distinct values of <paramref name="column"/>.</summary>
    /// <param name="column">The column name (from the header row).</param>
    /// <exception cref="InvalidOperationException">Thrown when a group-by column is already set.</exception>
    public SheetQuery GroupBy(string column)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(column);
        if (_groupBy is not null)
        {
            throw new InvalidOperationException("GroupBy supports a single column and was already called.");
        }

        _groupBy = column;
        return this;
    }

    /// <summary>Selects aggregate projections to compute, e.g. <c>QueryAggregates.Sum("NetSales"), QueryAggregates.Count()</c>.</summary>
    /// <param name="aggregates">The aggregate projections to compute.</param>
    public SheetQuery Select(params AggregateSpec[] aggregates)
    {
        ArgumentNullException.ThrowIfNull(aggregates);
        if (aggregates.Length == 0)
        {
            throw new ArgumentException("At least one aggregate is required.", nameof(aggregates));
        }

        _aggregates.AddRange(aggregates);
        return this;
    }

    /// <summary>
    /// Caps the number of result rows. For row queries (no aggregates) the scan stops as soon
    /// as the first <paramref name="count"/> matching rows are found; for grouped queries the
    /// first <paramref name="count"/> groups in first-seen order are returned after a full scan.
    /// </summary>
    /// <param name="count">The maximum number of result rows.</param>
    public SheetQuery Take(int count)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(count);
        _limit = count;
        return this;
    }

    /// <summary>Overrides the hard cap on group / distinct-value cardinality (default 10,000).</summary>
    /// <param name="maxGroups">The maximum number of groups before the query throws <see cref="TooManyGroupsException"/>.</param>
    public SheetQuery WithGroupLimit(int maxGroups)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxGroups);
        _maxGroups = maxGroups;
        return this;
    }

    /// <summary>
    /// Supplies column profiles from a prior <see cref="ExcelWorkbook.AnalyzeSheet"/> call.
    /// Numeric filters that no value in the profiled min/max range can satisfy
    /// return an empty result without opening the sheet.
    /// </summary>
    /// <param name="columns">The column profiles of the queried sheet.</param>
    public SheetQuery WithStats(IReadOnlyList<ColumnProfile> columns)
    {
        ArgumentNullException.ThrowIfNull(columns);
        _stats = columns;
        return this;
    }

    // ── Terminals ─────────────────────────────────────────────────────────────

    /// <summary>Runs the query synchronously in a single streaming pass.</summary>
    /// <returns>The materialized result.</returns>
    /// <exception cref="InvalidOperationException">Thrown when a referenced column does not exist or the shape is invalid.</exception>
    /// <exception cref="TooManyGroupsException">Thrown when group cardinality exceeds the cap.</exception>
    public QueryResult Execute()
    {
        var scan = CreateScan(distinctColumn: null);
        if (!scan.TryPruneWithStats(_stats))
        {
            RunScan(scan);
        }

        return scan.BuildResult();
    }

    /// <summary>Runs the query asynchronously in a single streaming pass.</summary>
    /// <param name="ct">A cancellation token.</param>
    /// <returns>A task that returns the materialized result.</returns>
    /// <exception cref="InvalidOperationException">Thrown when a referenced column does not exist or the shape is invalid.</exception>
    /// <exception cref="TooManyGroupsException">Thrown when group cardinality exceeds the cap.</exception>
    public async Task<QueryResult> ExecuteAsync(CancellationToken ct = default)
    {
        var scan = CreateScan(distinctColumn: null);
        if (!scan.TryPruneWithStats(_stats))
        {
            await RunScanAsync(scan, ct).ConfigureAwait(false);
        }

        return scan.BuildResult();
    }

    /// <summary>
    /// Counts the distinct values of <paramref name="column"/> across the rows matching the
    /// filters, ordered by descending frequency. Use for filter discovery beyond the
    /// distinct-value cap of <see cref="ExcelWorkbook.AnalyzeSheet"/>.
    /// </summary>
    /// <param name="column">The column name (from the header row).</param>
    /// <param name="top">The maximum number of values to return.</param>
    /// <returns>The most frequent values with their counts.</returns>
    /// <exception cref="TooManyGroupsException">Thrown when distinct cardinality exceeds the cap.</exception>
    public IReadOnlyList<DistinctValueCount> DistinctValues(string column, int top = 100)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(column);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(top);

        var scan = CreateScan(column);
        RunScan(scan);
        return scan.BuildDistinctValues(top);
    }

    /// <summary>
    /// Counts the distinct values of <paramref name="column"/> across the rows matching the
    /// filters, ordered by descending frequency. Use for filter discovery beyond the
    /// distinct-value cap of <see cref="ExcelWorkbook.AnalyzeSheet"/>.
    /// </summary>
    /// <param name="column">The column name (from the header row).</param>
    /// <param name="top">The maximum number of values to return.</param>
    /// <param name="ct">A cancellation token.</param>
    /// <returns>A task that returns the most frequent values with their counts.</returns>
    /// <exception cref="TooManyGroupsException">Thrown when distinct cardinality exceeds the cap.</exception>
    public async Task<IReadOnlyList<DistinctValueCount>> DistinctValuesAsync(
        string column,
        int top = 100,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(column);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(top);

        var scan = CreateScan(column);
        await RunScanAsync(scan, ct).ConfigureAwait(false);
        return scan.BuildDistinctValues(top);
    }

    /// <summary>
    /// Drives the scan. Aggregate-shaped queries over a bounded range first probe for the
    /// header row, then re-open the data rows with a column projection so cells the query
    /// never reads are not materialized (no shared-string resolution, no number parsing).
    /// Row queries return every column and use a single unprojected pass.
    /// </summary>
    private void RunScan(QueryScan scan)
    {
        if (scan.SupportsProjection && !_range.IsUnbounded)
        {
            using (var probe = _workbook.GetRangeReader(_sheet, _range))
            {
                while (!scan.HeaderBound && probe.Read())
                {
                    scan.ProcessRow(probe.Current);
                }
            }

            if (!scan.HeaderBound || scan.DataRangeAfterHeader(_range) is not { } dataRange)
            {
                return;
            }

            using var reader = _workbook.GetRangeReader(_sheet, dataRange, ReadMode.Values, scan.BuildProjection());
            while (reader.Read() && scan.ProcessRow(reader.Current))
            {
            }

            return;
        }

        using var fullReader = _workbook.GetRangeReader(_sheet, _range);
        while (fullReader.Read() && scan.ProcessRow(fullReader.Current))
        {
        }
    }

    private async Task RunScanAsync(QueryScan scan, CancellationToken ct)
    {
        if (scan.SupportsProjection && !_range.IsUnbounded)
        {
            var probe = await _workbook.GetRangeReaderAsync(_sheet, _range, ct: ct).ConfigureAwait(false);
            await using (probe.ConfigureAwait(false))
            {
                while (!scan.HeaderBound && await probe.ReadAsync(ct).ConfigureAwait(false))
                {
                    scan.ProcessRow(probe.Current);
                }
            }

            if (!scan.HeaderBound || scan.DataRangeAfterHeader(_range) is not { } dataRange)
            {
                return;
            }

            var reader = await _workbook
                .GetRangeReaderAsync(_sheet, dataRange, ReadMode.Values, scan.BuildProjection(), ct)
                .ConfigureAwait(false);
            await using (reader.ConfigureAwait(false))
            {
                while (await reader.ReadAsync(ct).ConfigureAwait(false) && scan.ProcessRow(reader.Current))
                {
                }
            }

            return;
        }

        var fullReader = await _workbook.GetRangeReaderAsync(_sheet, _range, ct: ct).ConfigureAwait(false);
        await using (fullReader.ConfigureAwait(false))
        {
            while (await fullReader.ReadAsync(ct).ConfigureAwait(false) && scan.ProcessRow(fullReader.Current))
            {
            }
        }
    }

    private QueryScan CreateScan(string? distinctColumn)
    {
        if (distinctColumn is null && _groupBy is not null && _aggregates.Count == 0)
        {
            throw new InvalidOperationException(
                "GroupBy requires at least one Select aggregate. Use DistinctValues(column) to enumerate a column's values.");
        }

        return new QueryScan(
            _range,
            _headerRow,
            _filters,
            _groupBy,
            _aggregates,
            distinctColumn,
            _limit,
            _maxGroups);
    }

    internal SheetQuery WhereCell(string column, QueryOperator op, ExcelCellValue literal)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(column);
        _filters.Add(new FilterSpec(column, op, literal));
        return this;
    }

    private SheetQuery AddFilter(string column, QueryOperator op, ExcelCellValue value)
        => WhereCell(column, op, value);
}
