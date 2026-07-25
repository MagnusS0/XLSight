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
        ct.ThrowIfCancellationRequested();
        var scan = CreateScan(distinctColumn: null);
        if (!scan.TryPruneWithStats(_stats))
        {
            await RunScanAsync(scan, ct).ConfigureAwait(false);
        }

        return scan.BuildResult(ct);
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
        ct.ThrowIfCancellationRequested();

        var scan = CreateScan(column);
        await RunScanAsync(scan, ct).ConfigureAwait(false);
        return scan.BuildDistinctValues(top, ct);
    }

    /// <summary>The header row sits above the queried range, so every range row is a data row.</summary>
    private bool HasExternalHeader =>
        _headerRow > 0 && !_range.IsUnbounded && _headerRow < _range.TopLeft.Row;

    private ExcelRange HeaderRowRange() => new(
        new ExcelAddress(_range.TopLeft.Column, _headerRow),
        new ExcelAddress(_range.BottomRight.Column, _headerRow));

    /// <summary>
    /// Drives the scan: bind the header row, then scan the data range in one shared pass.
    /// An external header (above the range) is bound from its own row first, so it never widens
    /// the data scan. Otherwise, aggregate-shaped queries over a bounded range probe the range
    /// itself for the header, then re-open the data rows with a column projection so cells the
    /// query never reads are not materialized. Row queries fall back to a single unprojected pass
    /// that binds and scans together, which is cheaper when no projection is possible.
    /// </summary>
    private void RunScan(QueryScan scan)
    {
        if (HasExternalHeader)
        {
            BindExternalHeader(scan);
        }
        else if (scan.SupportsProjection && !_range.IsUnbounded)
        {
            using (var probe = _workbook.GetRangeReader(_sheet, _range))
            {
                while (!scan.HeaderBound && probe.Read())
                {
                    scan.ProcessRow(probe.Current);
                }
            }

            if (!scan.HeaderBound)
            {
                return;
            }
        }
        else
        {
            using var fullReader = _workbook.GetRangeReader(_sheet, _range);
            while (fullReader.Read() && scan.ProcessRow(fullReader.Current))
            {
            }

            return;
        }

        if (scan.DataRangeAfterHeader(_range) is not { } dataRange)
        {
            return;
        }

        ScanDataRange(scan, dataRange);
    }

    private void BindExternalHeader(QueryScan scan)
    {
        using (var probe = _workbook.GetRangeReader(_sheet, HeaderRowRange()))
        {
            while (!scan.HeaderBound && probe.Read())
            {
                scan.ProcessRow(probe.Current);
            }
        }

        if (!scan.HeaderBound)
        {
            throw new InvalidOperationException($"Header row {_headerRow} contains no cells.");
        }
    }

    private void ScanDataRange(QueryScan scan, ExcelRange dataRange)
    {
        if (scan.SupportsProjection)
        {
            using var reader = _workbook.GetRangeReader(_sheet, dataRange, ReadMode.Values, scan.BuildProjection());
            while (reader.Read() && scan.ProcessRow(reader.Current))
            {
            }
        }
        else
        {
            using var reader = _workbook.GetRangeReader(_sheet, dataRange);
            while (reader.Read() && scan.ProcessRow(reader.Current))
            {
            }
        }
    }

    private async Task RunScanAsync(QueryScan scan, CancellationToken ct)
    {
        if (HasExternalHeader)
        {
            await BindExternalHeaderAsync(scan, ct).ConfigureAwait(false);
        }
        else if (scan.SupportsProjection && !_range.IsUnbounded)
        {
            var probe = await _workbook.GetRangeReaderAsync(_sheet, _range, ct: ct).ConfigureAwait(false);
            await using (probe.ConfigureAwait(false))
            {
                while (!scan.HeaderBound && await probe.ReadAsync(ct).ConfigureAwait(false))
                {
                    scan.ProcessRow(probe.Current);
                }
            }

            if (!scan.HeaderBound)
            {
                return;
            }
        }
        else
        {
            var fullReader = await _workbook.GetRangeReaderAsync(_sheet, _range, ct: ct).ConfigureAwait(false);
            await using (fullReader.ConfigureAwait(false))
            {
                while (await fullReader.ReadAsync(ct).ConfigureAwait(false) && scan.ProcessRow(fullReader.Current))
                {
                }
            }

            return;
        }

        if (scan.DataRangeAfterHeader(_range) is not { } dataRange)
        {
            return;
        }

        await ScanDataRangeAsync(scan, dataRange, ct).ConfigureAwait(false);
    }

    private async Task BindExternalHeaderAsync(QueryScan scan, CancellationToken ct)
    {
        var probe = await _workbook.GetRangeReaderAsync(_sheet, HeaderRowRange(), ct: ct).ConfigureAwait(false);
        await using (probe.ConfigureAwait(false))
        {
            while (!scan.HeaderBound && await probe.ReadAsync(ct).ConfigureAwait(false))
            {
                scan.ProcessRow(probe.Current);
            }
        }

        if (!scan.HeaderBound)
        {
            throw new InvalidOperationException($"Header row {_headerRow} contains no cells.");
        }
    }

    private async Task ScanDataRangeAsync(QueryScan scan, ExcelRange dataRange, CancellationToken ct)
    {
        if (scan.SupportsProjection)
        {
            var reader = await _workbook
                .GetRangeReaderAsync(_sheet, dataRange, ReadMode.Values, scan.BuildProjection(), ct)
                .ConfigureAwait(false);
            await using (reader.ConfigureAwait(false))
            {
                while (await reader.ReadAsync(ct).ConfigureAwait(false) && scan.ProcessRow(reader.Current))
                {
                }
            }
        }
        else
        {
            var reader = await _workbook.GetRangeReaderAsync(_sheet, dataRange, ct: ct).ConfigureAwait(false);
            await using (reader.ConfigureAwait(false))
            {
                while (await reader.ReadAsync(ct).ConfigureAwait(false) && scan.ProcessRow(reader.Current))
                {
                }
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
