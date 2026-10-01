namespace XLSight.Query.Internal;

internal sealed record AggregateExpression(
    AggregateKind Kind,
    QueryExpression? Argument,
    QueryExpression? Filter) : QueryExpression;
