namespace XLSight.Query.Internal;

internal sealed record ExtendedQueryPlan(
    string Sheet,
    string RangeAddress,
    ExcelRange Range,
    SheetQueryHeader Header,
    bool SelectAll,
    IReadOnlyList<QuerySelection> Selections,
    QueryExpression? Where,
    IReadOnlyList<QueryExpression> GroupBy,
    QueryExpression? Having,
    QueryExpression? OrderBy,
    bool OrderDescending,
    int? Limit);
