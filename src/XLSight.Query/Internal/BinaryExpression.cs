namespace XLSight.Query.Internal;

internal sealed record BinaryExpression(string Operator, QueryExpression Left, QueryExpression Right) : QueryExpression;
