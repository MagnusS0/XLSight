namespace XLSight.Query.Internal;

internal sealed record UnaryExpression(string Operator, QueryExpression Operand) : QueryExpression;
