using XLSight.Query.Internal;

namespace XLSight.Query;

/// <summary>A parsed, validated Query DSL statement ready to execute against a workbook.</summary>
public sealed class SheetQuerySpec
{
    internal SheetQuerySpec(
        string sheet,
        string rangeAddress,
        ExcelRange range,
        SheetQueryHeader header,
        bool selectAll,
        IReadOnlyList<AggregateSpec> aggregates,
        IReadOnlyList<SheetQueryPredicate> predicates,
        string? groupBy,
        int? limit)
    {
        Sheet = sheet;
        RangeAddress = rangeAddress;
        Range = range;
        Header = header;
        SelectAll = selectAll;
        Aggregates = aggregates.ToArray();
        Predicates = predicates.ToArray();
        GroupBy = groupBy;
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

    /// <summary>Gets the <c>WHERE</c> predicates, combined by <c>AND</c>.</summary>
    public IReadOnlyList<SheetQueryPredicate> Predicates { get; }

    /// <summary>Gets the optional <c>GROUP BY</c> column.</summary>
    public string? GroupBy { get; }

    /// <summary>Gets the optional positive <c>LIMIT</c> value.</summary>
    public int? Limit { get; }

    /// <summary>Parses Query DSL text into a structured query specification.</summary>
    /// <param name="queryText">The Query DSL text.</param>
    /// <returns>The parsed query specification.</returns>
    /// <exception cref="QueryDslException">Thrown when the query text is invalid or unsupported.</exception>
    public static SheetQuerySpec Parse(string queryText) => QueryDslParser.Parse(queryText);
}
