using System.Runtime.InteropServices;
using XLSight.Analysis;
using XLSight.Internal.Readers;

namespace XLSight.Query.Internal;

/// <summary>
/// The fused single-pass scan state: header binding, filter evaluation, and the
/// per-mode accumulation (row collection, global aggregates, grouped aggregates,
/// or distinct-value counting). Shared by the sync and async terminals.
/// </summary>
internal sealed class QueryScan
{
    private const int SampleRowLimit = 5;

    private readonly ExcelRange _range;
    private readonly int _headerRowParam;
    private readonly FilterSpec[] _filterSpecs;
    private readonly string? _groupByColumn;
    private readonly AggregateSpec[] _aggregateSpecs;
    private readonly string? _distinctColumn;
    private readonly int _limit;
    private readonly int _maxGroups;

    // ── Bound at the header row ───────────────────────────────────────────────
    private bool _headerBound;
    private int _boundHeaderRow;
    private string[] _columnNames = [];
    private int[] _columnIndices = [];
    private ResolvedFilter[] _filters = [];
    private int _groupColumnIndex;
    private string _groupColumnName = "";
    private ResolvedAggregate[] _resolvedAggregates = [];
    private int _distinctColumnIndex;

    // ── Accumulation ──────────────────────────────────────────────────────────
    private int _rowsScanned;
    private int _rowsMatched;
    private bool _pruned;
    private readonly List<QueryResultRow> _rows = [];
    private AggregateAccumulator[]? _globalAggregates;
    private ExcelCellValue _lastGroupKey;
    private AggregateAccumulator[]? _lastGroupAccumulators;
    private readonly Dictionary<ExcelCellValue, AggregateAccumulator[]> _groups = [];
    private readonly List<ExcelCellValue> _groupOrder = [];
    private readonly Dictionary<ExcelCellValue, int> _distinctCounts = [];
    private readonly Dictionary<string, DirtyColumn> _dirtyColumns = new(StringComparer.Ordinal);
    private readonly List<string> _dirtyOrder = [];

    [StructLayout(LayoutKind.Auto)]
    private readonly record struct ResolvedFilter(int ColumnIndex, QueryOp Op, ExcelCellValue Literal);

    [StructLayout(LayoutKind.Auto)]
    private readonly record struct ResolvedAggregate(AggregateKind Kind, int ColumnIndex);

    private sealed class DirtyColumn
    {
        public int Count;
        public List<int> SampleRows { get; } = new(SampleRowLimit);
    }

    internal QueryScan(
        ExcelRange range,
        int headerRow,
        List<FilterSpec> filters,
        string? groupBy,
        List<AggregateSpec> aggregates,
        string? distinctColumn,
        int limit,
        int maxGroups)
    {
        _range = range;
        _headerRowParam = headerRow;
        _filterSpecs = [.. filters];
        _groupByColumn = groupBy;
        _aggregateSpecs = [.. aggregates];
        _distinctColumn = distinctColumn;
        _limit = limit;
        _maxGroups = maxGroups;
    }

    // ── Projection support ────────────────────────────────────────────────────

    /// <summary>True once the header row was seen and all column references resolved.</summary>
    public bool HeaderBound => _headerBound;

    /// <summary>
    /// Aggregate, grouped, and distinct queries read a fixed set of columns, so the data
    /// scan can skip materializing every other in-range cell. Row queries return all columns.
    /// </summary>
    public bool SupportsProjection => _distinctColumn is not null || _aggregateSpecs.Length > 0;

    /// <summary>The remaining data rows of <paramref name="range"/> after the bound header row, or null when none.</summary>
    public ExcelRange? DataRangeAfterHeader(ExcelRange range)
    {
        if (_boundHeaderRow >= range.BottomRight.Row)
        {
            return null;
        }

        return new ExcelRange(
            new ExcelAddress(range.TopLeft.Column, _boundHeaderRow + 1),
            range.BottomRight);
    }

    /// <summary>The projection covering exactly the columns the query reads.</summary>
    public RowProjection BuildProjection()
    {
        var columns = new List<int>(_filters.Length + _resolvedAggregates.Length + 2);
        foreach (ResolvedFilter filter in _filters)
        {
            columns.Add(filter.ColumnIndex);
        }

        foreach (ResolvedAggregate aggregate in _resolvedAggregates)
        {
            if (aggregate.ColumnIndex >= 1) { columns.Add(aggregate.ColumnIndex); }
        }

        if (_groupByColumn is not null) { columns.Add(_groupColumnIndex); }
        if (_distinctColumn is not null) { columns.Add(_distinctColumnIndex); }
        return new RowProjection(CollectionsMarshal.AsSpan(columns));
    }

    // ── Stats pruning ─────────────────────────────────────────────────────────

    /// <summary>
    /// Uses analyzed column min/max as zone maps: when any numeric filter provably matches
    /// no value in the column, the result is empty and the sheet is never opened.
    /// </summary>
    public bool TryPruneWithStats(IReadOnlyList<ColumnProfile>? stats)
    {
        if (stats is null)
        {
            return false;
        }

        foreach (FilterSpec filter in _filterSpecs)
        {
            if (FilterProvablyEmpty(filter, stats))
            {
                _pruned = true;
                return true;
            }
        }

        return false;
    }

    private static bool FilterProvablyEmpty(in FilterSpec filter, IReadOnlyList<ColumnProfile> stats)
    {
        if (filter.Literal.CellType != CellType.Number)
        {
            return false;
        }

        ColumnProfile? profile = null;
        foreach (ColumnProfile candidate in stats)
        {
            if (string.Equals(candidate.InferredHeader, filter.Column, StringComparison.OrdinalIgnoreCase))
            {
                profile = candidate;
                break;
            }
        }

        if (profile is not { MinNumericValue: double min, MaxNumericValue: double max })
        {
            return false;
        }

        double literal = filter.Literal.AsNumber();
        return filter.Op switch
        {
            QueryOp.Equals => literal < min || literal > max,
            QueryOp.NotEquals => min == max && min == literal,
            QueryOp.LessThan => min >= literal,
            QueryOp.LessThanOrEqual => min > literal,
            QueryOp.GreaterThan => max <= literal,
            QueryOp.GreaterThanOrEqual => max < literal,
            _ => false,
        };
    }

    // ── Row processing ────────────────────────────────────────────────────────

    /// <summary>Processes one borrowed row. Returns false to stop the scan early.</summary>
    public bool ProcessRow(in ExcelRow row)
    {
        if (!_headerBound)
        {
            if (_headerRowParam > 0 && row.RowIndex < _headerRowParam)
            {
                return true;
            }

            if (_headerRowParam > 0 && row.RowIndex > _headerRowParam)
            {
                throw new InvalidOperationException($"Header row {_headerRowParam} contains no cells.");
            }

            BindHeader(row);
            return true;
        }

        _rowsScanned++;
        if (!MatchesFilters(row))
        {
            return true;
        }

        _rowsMatched++;

        if (_distinctColumn is not null)
        {
            AccumulateDistinct(row);
            return true;
        }

        if (_aggregateSpecs.Length == 0)
        {
            return CollectRow(row);
        }

        if (_groupByColumn is not null)
        {
            AccumulateGroup(row);
            return true;
        }

        UpdateAggregates(_globalAggregates ??= new AggregateAccumulator[_aggregateSpecs.Length], row);
        return true;
    }

    private bool MatchesFilters(in ExcelRow row)
    {
        foreach (ResolvedFilter filter in _filters)
        {
            if (!FilterEvaluator.Matches(in row.GetCellRef(filter.ColumnIndex), filter.Op, filter.Literal))
            {
                return false;
            }
        }

        return true;
    }

    private bool CollectRow(in ExcelRow row)
    {
        var values = new ExcelCellValue[_columnIndices.Length];
        for (int i = 0; i < values.Length; i++)
        {
            values[i] = row.GetCell(_columnIndices[i]);
        }

        _rows.Add(new QueryResultRow { SourceRowIndex = row.RowIndex, Values = values });
        return _limit < 0 || _rows.Count < _limit;
    }

    private void AccumulateGroup(in ExcelRow row)
    {
        ExcelCellValue key = row.GetCell(_groupColumnIndex);

        // Group keys in real sheets arrive in runs, so a one-entry memo skips the
        // dictionary probe (and its full string hash) for consecutive equal keys.
        AggregateAccumulator[]? accumulators = _lastGroupAccumulators;
        if (accumulators is null || !key.Equals(_lastGroupKey))
        {
            if (!_groups.TryGetValue(key, out accumulators))
            {
                if (_groups.Count >= _maxGroups)
                {
                    throw new TooManyGroupsException(
                        $"Query exceeded {_maxGroups} groups — narrow the range, add filters, or raise the cap with WithGroupLimit(). For high-cardinality workloads use an external engine.");
                }

                accumulators = new AggregateAccumulator[_aggregateSpecs.Length];
                _groups.Add(key, accumulators);
                _groupOrder.Add(key);
            }

            _lastGroupKey = key;
            _lastGroupAccumulators = accumulators;
        }

        UpdateAggregates(accumulators, row);
    }

    private void UpdateAggregates(AggregateAccumulator[] accumulators, in ExcelRow row)
    {
        for (int i = 0; i < accumulators.Length; i++)
        {
            ResolvedAggregate aggregate = _resolvedAggregates[i];
            if (aggregate.Kind == AggregateKind.Count)
            {
                accumulators[i].Count++;
                continue;
            }

            ref readonly ExcelCellValue cell = ref row.GetCellRef(aggregate.ColumnIndex);
            if (cell.IsEmpty)
            {
                continue;
            }

            if (!accumulators[i].TryAccumulate(aggregate.Kind, in cell))
            {
                RecordDirty(_aggregateSpecs[i].Column!, row.RowIndex);
            }
        }
    }

    private void AccumulateDistinct(in ExcelRow row)
    {
        ExcelCellValue cell = row.GetCell(_distinctColumnIndex);
        if (cell.IsEmpty)
        {
            return;
        }

        ref int count = ref CollectionsMarshal.GetValueRefOrAddDefault(_distinctCounts, cell, out bool exists);
        if (!exists && _distinctCounts.Count > _maxGroups)
        {
            throw new TooManyGroupsException(
                $"DistinctValues exceeded {_maxGroups} distinct values — narrow the range, add filters, or raise the cap with WithGroupLimit().");
        }

        count++;
    }

    private void RecordDirty(string column, int rowIndex)
    {
        if (!_dirtyColumns.TryGetValue(column, out DirtyColumn? dirty))
        {
            dirty = new DirtyColumn();
            _dirtyColumns.Add(column, dirty);
            _dirtyOrder.Add(column);
        }

        dirty.Count++;
        if (dirty.SampleRows.Count < SampleRowLimit)
        {
            dirty.SampleRows.Add(rowIndex);
        }
    }

    // ── Header binding ────────────────────────────────────────────────────────

    private void BindHeader(in ExcelRow row)
    {
        int startColumn;
        int endColumn;
        if (_range.IsUnbounded)
        {
            startColumn = row.StartColumn;
            endColumn = row.StartColumn + Math.Max(row.CellCount - 1, 0);
        }
        else
        {
            startColumn = _range.TopLeft.Column;
            endColumn = _range.BottomRight.Column;
        }

        int width = endColumn - startColumn + 1;
        _columnNames = new string[width];
        _columnIndices = new int[width];
        for (int i = 0; i < width; i++)
        {
            int column = startColumn + i;
            _columnIndices[i] = column;
            ref readonly ExcelCellValue cell = ref row.GetCellRef(column);
            string name = cell.IsEmpty ? "" : cell.ToString().Trim();
            _columnNames[i] = name.Length > 0 ? name : ColumnLabel(column);
        }

        _filters = new ResolvedFilter[_filterSpecs.Length];
        for (int i = 0; i < _filterSpecs.Length; i++)
        {
            FilterSpec spec = _filterSpecs[i];
            _filters[i] = new ResolvedFilter(ResolveColumn(spec.Column), spec.Op, spec.Literal);
        }

        _resolvedAggregates = new ResolvedAggregate[_aggregateSpecs.Length];
        for (int i = 0; i < _aggregateSpecs.Length; i++)
        {
            AggregateSpec spec = _aggregateSpecs[i];
            _resolvedAggregates[i] = new ResolvedAggregate(
                spec.Kind,
                spec.Column is { } column ? ResolveColumn(column) : -1);
        }

        if (_groupByColumn is not null)
        {
            _groupColumnIndex = ResolveColumn(_groupByColumn);
            _groupColumnName = _columnNames[Array.IndexOf(_columnIndices, _groupColumnIndex)];
        }

        if (_distinctColumn is not null)
        {
            _distinctColumnIndex = ResolveColumn(_distinctColumn);
        }

        _boundHeaderRow = row.RowIndex;
        _headerBound = true;
    }

    private int ResolveColumn(string name)
    {
        for (int i = 0; i < _columnNames.Length; i++)
        {
            if (string.Equals(_columnNames[i], name, StringComparison.Ordinal))
            {
                return _columnIndices[i];
            }
        }

        for (int i = 0; i < _columnNames.Length; i++)
        {
            if (string.Equals(_columnNames[i], name, StringComparison.OrdinalIgnoreCase))
            {
                return _columnIndices[i];
            }
        }

        throw new InvalidOperationException(
            $"Column '{name}' was not found in the header row. Available columns: {string.Join(", ", _columnNames)}.");
    }

    private static string ColumnLabel(int column) => new ExcelAddress(column, 1).ToString()[..^1];

    // ── Result building ───────────────────────────────────────────────────────

    public QueryResult BuildResult()
    {
        if (_pruned || (!_headerBound && _aggregateSpecs.Length == 0))
        {
            return new QueryResult
            {
                Columns = [], Rows = [], RowsScanned = 0, RowsMatched = 0, Unaggregatable = [],
            };
        }

        if (_aggregateSpecs.Length == 0)
        {
            return NewResult(_columnNames, _rows);
        }

        return _groupByColumn is null ? BuildGlobalResult() : BuildGroupedResult();
    }

    private QueryResult BuildGlobalResult()
    {
        var columns = new string[_aggregateSpecs.Length];
        var values = new ExcelCellValue[_aggregateSpecs.Length];
        AggregateAccumulator[] accumulators = _globalAggregates ?? new AggregateAccumulator[_aggregateSpecs.Length];
        for (int i = 0; i < _aggregateSpecs.Length; i++)
        {
            columns[i] = _aggregateSpecs[i].Label;
            values[i] = accumulators[i].Result(_aggregateSpecs[i].Kind);
        }

        return NewResult(columns, [new QueryResultRow { Values = values }]);
    }

    private QueryResult BuildGroupedResult()
    {
        var columns = new string[_aggregateSpecs.Length + 1];
        columns[0] = _groupColumnName.Length > 0 ? _groupColumnName : _groupByColumn!;
        for (int i = 0; i < _aggregateSpecs.Length; i++)
        {
            columns[i + 1] = _aggregateSpecs[i].Label;
        }

        int rowCount = _limit < 0 ? _groupOrder.Count : Math.Min(_limit, _groupOrder.Count);
        var rows = new List<QueryResultRow>(rowCount);
        for (int g = 0; g < rowCount; g++)
        {
            ExcelCellValue key = _groupOrder[g];
            AggregateAccumulator[] accumulators = _groups[key];
            var values = new ExcelCellValue[_aggregateSpecs.Length + 1];
            values[0] = key;
            for (int i = 0; i < _aggregateSpecs.Length; i++)
            {
                values[i + 1] = accumulators[i].Result(_aggregateSpecs[i].Kind);
            }

            rows.Add(new QueryResultRow { Values = values });
        }

        return NewResult(columns, rows);
    }

    private QueryResult NewResult(IReadOnlyList<string> columns, IReadOnlyList<QueryResultRow> rows)
    {
        return new QueryResult
        {
            Columns = columns,
            Rows = rows,
            RowsScanned = _rowsScanned,
            RowsMatched = _rowsMatched,
            Unaggregatable = BuildUnaggregatable(),
        };
    }

    private List<UnaggregatableColumn> BuildUnaggregatable()
    {
        if (_dirtyOrder.Count == 0)
        {
            return [];
        }

        var result = new List<UnaggregatableColumn>(_dirtyOrder.Count);
        foreach (string column in _dirtyOrder)
        {
            DirtyColumn dirty = _dirtyColumns[column];
            result.Add(new UnaggregatableColumn
            {
                Column = column,
                SkippedCount = dirty.Count,
                SampleRowIndices = dirty.SampleRows,
            });
        }

        return result;
    }

    public IReadOnlyList<DistinctValueCount> BuildDistinctValues(int top)
    {
        // Merge by display string: a numeric 1 and a text "1" both format to "1".
        var merged = new Dictionary<string, int>(_distinctCounts.Count, StringComparer.Ordinal);
        foreach ((ExcelCellValue cell, int count) in _distinctCounts)
        {
            string value = cell.ToString();
            merged[value] = merged.GetValueOrDefault(value) + count;
        }

        return [.. merged
            .OrderByDescending(static pair => pair.Value)
            .ThenBy(static pair => pair.Key, StringComparer.Ordinal)
            .Take(top)
            .Select(static pair => new DistinctValueCount(pair.Key, pair.Value))];
    }
}
