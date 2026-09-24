using System.Runtime.InteropServices;
using XLSight.Internal.Readers;
using static XLSight.Query.Internal.ExpressionEvaluator;

namespace XLSight.Query.Internal;

/// <summary>Streaming execution for expressions. Source rows are borrowed; only survivors and group states are retained.</summary>
internal sealed class ExpressionQueryScan(ExtendedQueryPlan plan, int headerRow, int maxGroups) : IQueryScan
{
    private ExpressionEvaluator _source = null!;
    private ExpressionEvaluator _result = null!;
    private BoundValue? _where;
    private BoundValue? _having;
    private BoundValue? _order;
    private BoundValue[] _selection = [];
    private BoundValue[] _groupKeys = [];
    private BoundAggregate[] _aggregates = [];
    private readonly Dictionary<GroupKey, int> _groups = new(new GroupKeyComparer());
    private readonly List<GroupKey> _groupOrder = [];
    private readonly List<QueryResultRow> _rows = [];
    private readonly Dictionary<string, DirtyExpression> _dirty = new(StringComparer.Ordinal);
    private PriorityQueue<StoredRow, RowPriority>? _top;
    private ExcelCellValue[] _keyBuffer = [];
    private ExcelCellValue[] _resultSlots = [];
    private string[] _columns = [];
    private string[] _headers = [];
    private bool _aggregateMode;
    private AggregateAccumulator[]? _global;
    private readonly List<AggregateAccumulator[]> _accumulatorBlocks = [];
    private int _groupsPerBlock;
    private int _lastGroupIndex = -1;
    private int _boundHeaderRow;
    private int _scanned;
    private int _matched;
    private int Limit => plan.Limit ?? -1;
    private ExcelCellValueComparer OrderComparer => plan.OrderDescending ? ExcelCellValueComparer.Descending : ExcelCellValueComparer.Ascending;

    public bool HeaderBound { get; private set; }
    public bool SupportsProjection => true;

    public ExcelRange? DataRangeAfterHeader(ExcelRange range)
    {
        int first = Math.Max(_boundHeaderRow + 1, range.TopLeft.Row);
        return Limit == 0 || first > range.BottomRight.Row ? null
            : new ExcelRange(new ExcelAddress(range.TopLeft.Column, first), range.BottomRight);
    }

    public RowProjection BuildProjection() => new(_source.Columns.ToArray());

    public bool ProcessRow(in ExcelRow row)
    {
        if (!HeaderBound)
        {
            if (headerRow > 0 && row.RowIndex < headerRow) { return true; }
            if (headerRow > 0 && row.RowIndex > headerRow)
            {
                throw new InvalidOperationException($"Header row {headerRow} contains no cells.");
            }
            Bind(row);
            return Limit != 0;
        }

        _scanned++;
        _source.BeginRow(row);
        if (_where is not null && !IsTrue(_where.Value)) { return true; }
        _matched++;
        if (!_aggregateMode)
        {
            CollectRow(row.RowIndex);
            return _order is not null || Limit < 0 || _rows.Count < Limit;
        }

        Accumulate(ResolveGroup(), row.RowIndex);
        return true;
    }

    private Span<AggregateAccumulator> ResolveGroup()
    {
        if (_groupKeys.Length == 0) { return _global; }

        for (int i = 0; i < _keyBuffer.Length; i++) { _keyBuffer[i] = _groupKeys[i].Value; }
        if (_lastGroupIndex >= 0 && _keyBuffer.AsSpan().SequenceEqual(_groupOrder[_lastGroupIndex].Values))
        {
            return GroupAccumulators(_lastGroupIndex);
        }

        var lookup = _groups.GetAlternateLookup<ReadOnlySpan<ExcelCellValue>>();
        if (!lookup.TryGetValue(_keyBuffer, out int index))
        {
            if (_groups.Count >= maxGroups)
            {
                throw new TooManyGroupsException($"Query exceeded {maxGroups} groups — narrow the range or use an external engine.");
            }
            index = _groupOrder.Count;
            var key = new GroupKey(_keyBuffer.AsSpan().ToArray());
            _groups.Add(key, index);
            _groupOrder.Add(key);
            if (_aggregates.Length > 0 && index % _groupsPerBlock == 0)
            {
                int groups = Math.Min(_groupsPerBlock, maxGroups - index);
                _accumulatorBlocks.Add(new AggregateAccumulator[groups * _aggregates.Length]);
            }
        }
        _lastGroupIndex = index;
        return GroupAccumulators(index);
    }

    private Span<AggregateAccumulator> GroupAccumulators(int index)
    {
        if (_aggregates.Length == 0) { return []; }
        int block = Math.DivRem(index, _groupsPerBlock, out int offset);
        return _accumulatorBlocks[block].AsSpan(offset * _aggregates.Length, _aggregates.Length);
    }

    private void Accumulate(Span<AggregateAccumulator> accumulators, int sourceRow)
    {
        for (int i = 0; i < _aggregates.Length; i++)
        {
            BoundAggregate aggregate = _aggregates[i];
            if (aggregate.Filter is not null && !IsTrue(aggregate.Filter.Value)) { continue; }
            if (aggregate.Expression.Kind == AggregateKind.Count && aggregate.Argument is null)
            {
                accumulators[i].Count++;
                continue;
            }
            ExcelCellValue cell = aggregate.Argument!.Value;
            if (cell.IsEmpty) { continue; }
            if (aggregate.Expression.Kind == AggregateKind.Count)
            {
                accumulators[i].Count++;
            }
            else if (!accumulators[i].TryAccumulate(aggregate.Expression.Kind, cell))
            {
                string label = aggregate.Label;
                if (!_dirty.TryGetValue(label, out DirtyExpression? dirty))
                {
                    dirty = new DirtyExpression();
                    _dirty.Add(label, dirty);
                }
                dirty.Count++;
                if (dirty.Rows.Count < 5) { dirty.Rows.Add(sourceRow); }
            }
        }
    }

    private void Bind(in ExcelRow row)
    {
        int width = plan.Range.BottomRight.Column - plan.Range.TopLeft.Column + 1;
        _headers = new string[width];
        for (int i = 0; i < width; i++)
        {
            int column = plan.Range.TopLeft.Column + i;
            string raw = QueryScan.NormalizeHeaderName(row.GetCell(column).ToString());
            _headers[i] = raw.Length == 0 ? new ExcelAddress(column, 1).ToString()[..^1] : raw;
        }

        var aliases = new Dictionary<string, QueryExpression>(StringComparer.OrdinalIgnoreCase);
        foreach (QuerySelection selection in plan.Selections)
        {
            if (selection.Alias is { } alias && !aliases.TryAdd(alias, selection.Expression))
            {
                throw new QueryDslException($"Duplicate alias '{alias}'.");
            }
        }
        QueryExpression ExpandAlias(QueryExpression expression) => expression is ColumnExpression c && aliases.TryGetValue(c.Name, out QueryExpression? replacement)
            ? replacement : expression;

        QueryExpression[] grouping = plan.GroupBy.Select(ExpandAlias).ToArray();
        QuerySelection[] selections = plan.SelectAll
            ? _headers.Select(h => new QuerySelection(new ColumnExpression(h), null)).ToArray()
            : plan.Selections.ToArray();
        var aggregateExpressions = new List<AggregateExpression>();
        var aggregateIndexes = new Dictionary<string, int>(StringComparer.Ordinal);
        IEnumerable<QueryExpression> allResults = selections.Select(s => s.Expression);
        if (plan.Having is { } having) { allResults = allResults.Append(having); }
        if (plan.OrderBy is { } ordering) { allResults = allResults.Append(ExpandAlias(ordering)); }
        foreach (QueryExpression expression in allResults)
        {
            foreach (AggregateExpression aggregate in Aggregates(expression))
            {
                if (aggregateIndexes.TryAdd(BoundIdentity(aggregate), aggregateExpressions.Count)) { aggregateExpressions.Add(aggregate); }
            }
        }

        _aggregateMode = aggregateExpressions.Count > 0 || grouping.Length > 0;
        if (plan.SelectAll && _aggregateMode) { throw new QueryDslException("GROUP BY is not valid with SELECT *."); }
        if (plan.Having is not null && !_aggregateMode) { throw new QueryDslException("HAVING requires an aggregate query."); }
        if (plan.OrderBy is not null && !_aggregateMode && plan.Limit is null)
        {
            throw new QueryDslException("ORDER BY requires LIMIT on row results.");
        }
        if (grouping.Any(g => Aggregates(g).Any())) { throw new QueryDslException("GROUP BY cannot contain aggregates."); }

        _source = new ExpressionEvaluator(ResolveColumn);
        _where = plan.Where is null ? null : _source.Bind(plan.Where);
        _groupKeys = grouping.Select(_source.Bind).ToArray();
        _keyBuffer = new ExcelCellValue[_groupKeys.Length];
        _aggregates = aggregateExpressions.Select(a => new BoundAggregate(a,
            a.Argument is null ? null : _source.Bind(a.Argument), a.Filter is null ? null : _source.Bind(a.Filter),
            a.Argument is null ? "Count()" : ExpressionText.Format(a.Argument))).ToArray();

        BindResults(grouping, selections, aliases, aggregateIndexes);
        _boundHeaderRow = row.RowIndex;
        HeaderBound = true;
    }

    private void BindResults(QueryExpression[] grouping, QuerySelection[] selections,
        Dictionary<string, QueryExpression> aliases, Dictionary<string, int> aggregateIndexes)
    {
        // At most 64 groups and normally at most 1024 accumulator structs per block.
        // Grow without copying existing state or allocating an array for every group.
        _groupsPerBlock = Math.Max(1, Math.Min(64, 1024 / Math.Max(1, _aggregates.Length)));
        if (_aggregateMode)
        {
            var groupIndexes = new Dictionary<string, int>(StringComparer.Ordinal);
            for (int i = 0; i < grouping.Length; i++) { groupIndexes.TryAdd(BoundIdentity(grouping[i]), i); }
            int? Slot(QueryExpression expression)
            {
                string key = BoundIdentity(expression is ColumnExpression c && aliases.TryGetValue(c.Name, out QueryExpression? alias) ? alias : expression);
                if (groupIndexes.TryGetValue(key, out int g)) { return g; }
                return aggregateIndexes.TryGetValue(key, out int a) ? grouping.Length + a : null;
            }
            _result = new ExpressionEvaluator(name => throw new QueryDslException($"Column '{name}' must be grouped or aggregated."), Slot);
            // Preserve the established aggregate-only shape: implicit keys, then selections.
            if (grouping.Length > 0 && selections.All(s => Aggregates(s.Expression).Any()))
            {
                selections = [.. grouping.Select(g => new QuerySelection(g, null)), .. selections];
            }
            _selection = selections.Select(s => _result.Bind(s.Expression)).ToArray();
            // Aliases may refer to a complete computed selection, not just an aggregate slot.
            _having = plan.Having is null ? null : BindWithAliases(_result, plan.Having, aliases);
            _order = plan.OrderBy is null ? null : BindWithAliases(_result, plan.OrderBy, aliases);
            if (_order is not null && Limit > 0)
            {
                _top = new PriorityQueue<StoredRow, RowPriority>(new PriorityComparer(OrderComparer, reverse: true));
            }
            _resultSlots = new ExcelCellValue[grouping.Length + _aggregates.Length];
            if (grouping.Length == 0) { _global = new AggregateAccumulator[_aggregates.Length]; }
        }
        else
        {
            _result = _source;
            _selection = plan.SelectAll
                ? Enumerable.Range(plan.Range.TopLeft.Column, _headers.Length).Select(_source.BindColumnIndex).ToArray()
                : selections.Select(s => _source.Bind(s.Expression)).ToArray();
            _order = plan.OrderBy is null ? null : BindWithAliases(_source, plan.OrderBy, aliases);
            if (_order is not null)
            {
                _top = new PriorityQueue<StoredRow, RowPriority>(new PriorityComparer(OrderComparer, reverse: true));
            }
        }

        _columns = selections.Select(s => s.Alias ?? ExpressionText.Format(s.Expression)).ToArray();
    }

    private static BoundValue BindWithAliases(ExpressionEvaluator evaluator, QueryExpression expression, Dictionary<string, QueryExpression> aliases)
    {
        QueryExpression Replace(QueryExpression e) => e switch
        {
            ColumnExpression c when aliases.TryGetValue(c.Name, out QueryExpression? value) => value,
            UnaryExpression u => u with { Operand = Replace(u.Operand) },
            BinaryExpression b => b with { Left = Replace(b.Left), Right = Replace(b.Right) },
            FunctionExpression f => f with { Arguments = f.Arguments.Select(Replace).ToArray() },
            InExpression i => i with { Operand = Replace(i.Operand) },
            EmptyExpression empty => empty with { Operand = Replace(empty.Operand) },
            _ => e,
        };
        return evaluator.Bind(Replace(expression));
    }

    private int ResolveColumn(string name)
    {
        int match = Array.IndexOf(_headers, name);
        if (match < 0) { match = Array.FindIndex(_headers, h => string.Equals(h, name, StringComparison.OrdinalIgnoreCase)); }
        if (match < 0) { throw new InvalidOperationException($"Column '{name}' was not found in the header."); }
        return plan.Range.TopLeft.Column + match;
    }

    private string BoundIdentity(QueryExpression expression) => Identity(expression, name =>
    {
        int match = Array.IndexOf(_headers, name);
        if (match < 0) { match = Array.FindIndex(_headers, h => string.Equals(h, name, StringComparison.OrdinalIgnoreCase)); }
        return match < 0 ? name : _headers[match];
    });

    private void CollectRow(int sourceRow)
    {
        if (_order is null)
        {
            _rows.Add(new QueryResultRow { SourceRowIndex = sourceRow, Values = EvaluateSelection() });
            return;
        }

        var priority = new RowPriority(_order.Value, sourceRow);
        StoredRow stored;
        if (_top!.Count >= Limit)
        {
            _top.TryPeek(out StoredRow? weakest, out RowPriority worst);
            if (ComparePriority(priority, worst, OrderComparer) >= 0) { return; }
            _top.Dequeue();
            stored = weakest!;
        }
        else { stored = new StoredRow(new ExcelCellValue[_selection.Length]); }
        for (int i = 0; i < _selection.Length; i++) { stored.Values[i] = _selection[i].Value; }
        stored.SourceRow = sourceRow;
        _top.Enqueue(stored, priority);
    }

    private ExcelCellValue[] EvaluateSelection()
    {
        var values = new ExcelCellValue[_selection.Length];
        for (int i = 0; i < values.Length; i++) { values[i] = _selection[i].Value; }
        return values;
    }

    internal QueryResult BuildResult(CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        List<QueryResultRow> rows = [];
        if (HeaderBound && Limit != 0)
        {
            if (_aggregateMode)
            {
                int capacity = _global is null ? _groupOrder.Count : 1;
                if (Limit >= 0) { capacity = Math.Min(capacity, Limit); }
                var candidates = new List<(QueryResultRow Row, RowPriority Priority)>(_top is null ? capacity : 0);
                int groupCount = _global is null ? _groupOrder.Count : 1;
                int index = 0;
                for (int groupIndex = 0; groupIndex < groupCount; groupIndex++)
                {
                    ct.ThrowIfCancellationRequested();
                    if (_global is null) { _groupOrder[groupIndex].Values.CopyTo(_resultSlots, 0); }
                    Span<AggregateAccumulator> accumulators = _global is null ? GroupAccumulators(groupIndex) : _global;
                    for (int i = 0; i < _aggregates.Length; i++)
                    {
                        _resultSlots[_groupKeys.Length + i] = accumulators[i].Result(_aggregates[i].Expression.Kind);
                    }
                    _result.BeginResult(_resultSlots);
                    if (_having is not null && !IsTrue(_having.Value)) { continue; }
                    if (_top is not null)
                    {
                        CollectRow(index++);
                        continue;
                    }
                    candidates.Add((new QueryResultRow { Values = EvaluateSelection() }, new RowPriority(_order?.Value ?? default, index++)));
                    if (_order is null && Limit > 0 && candidates.Count >= Limit) { break; }
                }
                if (_top is not null) { rows = BuildTopRows(includeSourceRow: false); }
                if (_order is not null) { candidates.Sort((a, b) => ComparePriority(a.Priority, b.Priority, OrderComparer)); }
                int count = Limit < 0 ? candidates.Count : Math.Min(Limit, candidates.Count);
                for (int i = 0; i < count; i++) { rows.Add(candidates[i].Row); }
            }
            else if (_top is not null)
            {
                rows = BuildTopRows(includeSourceRow: true);
            }
            else { rows = _rows; }
        }
        return new QueryResult
        {
            Columns = _columns, Rows = rows, RowsScanned = _scanned, RowsMatched = _matched,
            Unaggregatable = _dirty.Select(pair => new UnaggregatableColumn
            {
                Column = pair.Key, SkippedCount = pair.Value.Count, SampleRowIndices = pair.Value.Rows,
            }).ToArray(),
        };
    }

    private List<QueryResultRow> BuildTopRows(bool includeSourceRow)
    {
        var ordered = new List<(StoredRow Row, RowPriority Priority)>(_top!.Count);
        while (_top.TryDequeue(out StoredRow? row, out RowPriority priority)) { ordered.Add((row, priority)); }
        ordered.Sort((a, b) => ComparePriority(a.Priority, b.Priority, OrderComparer));
        return ordered.Select(r => new QueryResultRow
        {
            Values = r.Row.Values, SourceRowIndex = includeSourceRow ? r.Row.SourceRow : null,
        }).ToList();
    }

    private static int ComparePriority(RowPriority a, RowPriority b, ExcelCellValueComparer comparer)
    {
        int cmp = comparer.Compare(a.Key, b.Key);
        return cmp != 0 ? cmp : a.SourceRow.CompareTo(b.SourceRow);
    }

    private readonly record struct BoundAggregate(AggregateExpression Expression, BoundValue? Argument, BoundValue? Filter, string Label);
    [StructLayout(LayoutKind.Auto)]
    private readonly record struct RowPriority(ExcelCellValue Key, int SourceRow);
    private sealed class PriorityComparer(ExcelCellValueComparer comparer, bool reverse) : IComparer<RowPriority>
    {
        public int Compare(RowPriority x, RowPriority y) => reverse ? ComparePriority(y, x, comparer) : ComparePriority(x, y, comparer);
    }
    private sealed class StoredRow(ExcelCellValue[] values)
    {
        internal ExcelCellValue[] Values { get; } = values;
        internal int SourceRow { get; set; }
    }
    private sealed class DirtyExpression
    {
        internal int Count;
        internal List<int> Rows { get; } = [];
    }
    private readonly record struct GroupKey(ExcelCellValue[] Values);
    private sealed class GroupKeyComparer : IEqualityComparer<GroupKey>, IAlternateEqualityComparer<ReadOnlySpan<ExcelCellValue>, GroupKey>
    {
        public bool Equals(GroupKey x, GroupKey y) => x.Values.AsSpan().SequenceEqual(y.Values);
        public int GetHashCode(GroupKey obj) => GetHashCode(obj.Values);
        public bool Equals(ReadOnlySpan<ExcelCellValue> alternate, GroupKey other) => alternate.SequenceEqual(other.Values);
        public int GetHashCode(ReadOnlySpan<ExcelCellValue> alternate)
        {
            var hash = new HashCode();
            foreach (ExcelCellValue value in alternate) { hash.Add(value); }
            return hash.ToHashCode();
        }
        public GroupKey Create(ReadOnlySpan<ExcelCellValue> alternate) => new(alternate.ToArray());
    }
}
