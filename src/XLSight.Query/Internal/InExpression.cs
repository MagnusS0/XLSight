namespace XLSight.Query.Internal;

internal sealed record InExpression(
    QueryExpression Operand,
    IReadOnlyList<QueryExpression> Values,
    bool Negated) : QueryExpression;
