namespace XLSight.Query.Internal;

internal sealed record FunctionExpression(string Name, IReadOnlyList<QueryExpression> Arguments) : QueryExpression;
