namespace XLSight.Query.Internal;

internal sealed record EmptyExpression(QueryExpression Operand, bool Negated) : QueryExpression;
