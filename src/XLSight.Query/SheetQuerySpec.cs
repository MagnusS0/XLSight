using XLSight.Query.Internal;

namespace XLSight.Query;

/// <summary>A parsed, validated Query DSL statement ready to execute against a workbook.</summary>
public sealed class SheetQuerySpec
{
    internal ExtendedQueryPlan? ExpressionPlan { get; private init; }

    /// <summary>Gets the maximum number of retained groups. Defaults to 10,000.</summary>
    public int GroupLimit { get; private init; } = 10_000;

    /// <summary>Returns this specification with a different positive group-state cap.</summary>
    public SheetQuerySpec WithGroupLimit(int maxGroups)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxGroups);
        return new SheetQuerySpec(Sheet, RangeAddress, Range, Header, SelectAll, Aggregates, Columns,
            Predicates, GroupBy, OrderBy, OrderDescending, OrderIndex, Limit)
        {
            ExpressionPlan = ExpressionPlan, GroupLimit = maxGroups,
        };
    }

    /// <summary>Gets whether the specification uses the expression language.</summary>
    public bool UsesExpressions => ExpressionPlan is not null;

    /// <summary>Gets the full WHERE expression text for expression queries; null for simple queries.</summary>
    public string? WhereExpression => ExpressionPlan?.Where is { } expression ? ExpressionText.Format(expression) : null;

    /// <summary>Gets the optional HAVING expression text.</summary>
    public string? HavingExpression => ExpressionPlan?.Having is { } expression ? ExpressionText.Format(expression) : null;

    /// <summary>Gets the selected expressions, without aliases, for expression queries.</summary>
    public IReadOnlyList<string> SelectExpressions => ExpressionPlan?.Selections.Select(s => ExpressionText.Format(s.Expression)).ToArray() ?? [];

    /// <summary>Gets all grouping expressions (the single column for a simple query).</summary>
    public IReadOnlyList<string> GroupByExpressions => ExpressionPlan?.GroupBy.Select(ExpressionText.Format).ToArray()
        ?? (GroupBy is null ? [] : [GroupBy]);

    internal SheetQuerySpec(
        string sheet,
        string rangeAddress,
        ExcelRange range,
        SheetQueryHeader header,
        bool selectAll,
        IReadOnlyList<AggregateSpec> aggregates,
        IReadOnlyList<string> columns,
        IReadOnlyList<SheetQueryPredicate> predicates,
        string? groupBy,
        string? orderBy,
        bool orderDescending,
        int orderIndex,
        int? limit)
    {
        Sheet = sheet;
        RangeAddress = rangeAddress;
        Range = range;
        Header = header;
        SelectAll = selectAll;
        Aggregates = aggregates.ToArray();
        Columns = columns.ToArray();
        Predicates = predicates.ToArray();
        GroupBy = groupBy;
        OrderBy = orderBy;
        OrderDescending = orderDescending;
        OrderIndex = orderIndex;
        Limit = limit;
    }

    /// <summary>Gets the worksheet name from the <c>FROM</c> clause.</summary>
    public string Sheet { get; }

    /// <summary>Gets the normalized bounded A1 range address from the <c>FROM</c> clause.</summary>
    public string RangeAddress { get; }

    /// <summary>Gets the parsed Excel range from the <c>FROM</c> clause.</summary>
    public ExcelRange Range { get; }

    /// <summary>Gets the parsed <c>HEADER</c> clause.</summary>
    public SheetQueryHeader Header { get; }

    /// <summary>Gets a value indicating whether the statement uses <c>SELECT *</c> row-result mode.</summary>
    public bool SelectAll { get; }

    /// <summary>Gets the aggregate functions selected by the statement.</summary>
    public IReadOnlyList<AggregateSpec> Aggregates { get; }

    /// <summary>Gets directly selected source column names in <c>SELECT</c> order. Expression queries can include these alongside aggregates; computed selections appear in <see cref="SelectExpressions"/>.</summary>
    public IReadOnlyList<string> Columns { get; }

    /// <summary>Gets simple <c>WHERE</c> predicates combined by <c>AND</c>. Empty for expression queries; see <see cref="WhereExpression"/>.</summary>
    public IReadOnlyList<SheetQueryPredicate> Predicates { get; }

    /// <summary>Gets the optional <c>GROUP BY</c> column, or the first grouping expression for expression queries. See <see cref="GroupByExpressions"/> for all keys.</summary>
    public string? GroupBy { get; }

    /// <summary>Gets the optional <c>ORDER BY</c> key, as written (a column name or an aggregate call).</summary>
    public string? OrderBy { get; }

    /// <summary>Gets a value indicating whether <see cref="OrderBy"/> sorts descending. Ascending when no <c>ORDER BY</c> is present.</summary>
    public bool OrderDescending { get; }

    /// <summary>Gets the optional nonnegative <c>LIMIT</c> value. Zero requests headers only.</summary>
    public int? Limit { get; }

    /// <summary>
    /// The resolved result-column index for <see cref="OrderBy"/>: 0 = group key, i + 1 =
    /// aggregate i, -1 when unset, or <see cref="Internal.OrderByKeyResolver.RowOrderIndex"/> for
    /// raw-row top-N ordering (in which case <see cref="OrderBy"/> holds the raw column name).
    /// </summary>
    internal int OrderIndex { get; }

    /// <summary>Parses Query DSL text into a structured query specification.</summary>
    /// <param name="queryText">The Query DSL text.</param>
    /// <returns>The parsed query specification.</returns>
    /// <exception cref="QueryDslException">Thrown when the query text is invalid or unsupported.</exception>
    public static SheetQuerySpec Parse(string queryText)
    {
        ArgumentNullException.ThrowIfNull(queryText);
        if (queryText.Length > 1_048_576) { throw new QueryDslException("Query text exceeds the maximum length of 1048576 characters."); }
        try { return QueryDslParser.Parse(queryText); }
        catch (QueryDslException legacyError)
        {
            ExtendedQueryPlan plan;
            try { plan = ExtendedQueryParser.Parse(queryText); }
            catch (QueryDslException expressionError)
            {
                if (expressionError.Position > legacyError.Position || queryText.AsSpan().TrimStart().StartsWith("SELECT", StringComparison.OrdinalIgnoreCase))
                {
                    throw;
                }
                throw legacyError;
            }
            AggregateExpression[] aggregates = plan.Selections.SelectMany(s => ExpressionEvaluator.Aggregates(s.Expression)).ToArray();
            return new SheetQuerySpec(plan.Sheet, plan.RangeAddress, plan.Range, plan.Header, plan.SelectAll,
                aggregates.Select(a => new AggregateSpec(a.Kind, a.Argument is null ? null : ExpressionText.Format(a.Argument))).ToArray(),
                plan.Selections.Where(s => s.Expression is ColumnExpression).Select(s => ((ColumnExpression)s.Expression).Name).ToArray(),
                [], plan.GroupBy.Count == 0 ? null : ExpressionText.Format(plan.GroupBy[0]),
                plan.OrderBy is null ? null : ExpressionText.Format(plan.OrderBy), plan.OrderDescending, -1, plan.Limit)
            {
                ExpressionPlan = plan,
            };
        }
    }
}
