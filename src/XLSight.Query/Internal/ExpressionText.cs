namespace XLSight.Query.Internal;

/// <summary>Produces stable, compact labels for parsed expressions.</summary>
internal static class ExpressionText
{
    public static string Format(QueryExpression expression) => expression switch
    {
        ColumnExpression column => column.Name,
        LiteralExpression literal => FormatLiteral(literal.Value),
        UnaryExpression unary => $"{unary.Operator}({Format(unary.Operand)})",
        BinaryExpression binary => $"({Format(binary.Left)} {binary.Operator} {Format(binary.Right)})",
        FunctionExpression function => $"{function.Name}({string.Join(", ", function.Arguments.Select(Format))})",
        AggregateExpression aggregate => FormatAggregate(aggregate),
        InExpression @in => $"{Format(@in.Operand)} {(@in.Negated ? "NOT " : string.Empty)}IN ({string.Join(", ", @in.Values.Select(Format))})",
        EmptyExpression empty => $"{Format(empty.Operand)} IS {(empty.Negated ? "NOT " : string.Empty)}EMPTY",
        _ => expression.GetType().Name,
    };

    private static string FormatAggregate(AggregateExpression aggregate)
    {
        string name = aggregate.Kind switch
        {
            AggregateKind.Sum => "Sum",
            AggregateKind.Count => "Count",
            AggregateKind.Average => "Average",
            AggregateKind.Min => "Min",
            AggregateKind.Max => "Max",
            _ => aggregate.Kind.ToString(),
        };

        string result = aggregate.Argument is null
            ? $"{name}()"
            : $"{name}({Format(aggregate.Argument)})";
        return aggregate.Filter is null ? result : $"{result} FILTER (WHERE {Format(aggregate.Filter)})";
    }

    private static string FormatLiteral(ExcelCellValue value) => value.CellType switch
    {
        CellType.Empty => "EMPTY",
        CellType.Text => $"'{value.AsText().Replace("'", "''", StringComparison.Ordinal)}'",
        CellType.Number => value.AsNumber().ToString("R", System.Globalization.CultureInfo.InvariantCulture),
        CellType.Date => FormattableString.Invariant($"DATE '{value.AsDate():yyyy-MM-dd}'"),
        CellType.Boolean => value.AsBoolean() ? "TRUE" : "FALSE",
        CellType.Error => value.AsError(),
        CellType.Formula => value.AsFormula(),
        _ => value.ToString(),
    };
}
