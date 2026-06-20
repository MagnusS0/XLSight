namespace XLSight.Query.Internal;

internal static class FilterEvaluator
{
    /// <summary>
    /// Strictly typed comparison: only cells of the literal's type can satisfy a predicate
    /// (a text cell never matches a numeric literal, not even for NotEquals), and empty
    /// cells never match. Text ordering is ordinal.
    /// </summary>
    public static bool Matches(in ExcelCellValue cell, QueryOperator op, in ExcelCellValue literal)
    {
        switch (literal.CellType)
        {
            case CellType.Number:
                return cell.TryGetNumber(out double number)
                    && Satisfies(number.CompareTo(literal.AsNumber()), op);
            case CellType.Date:
                return cell.TryGetDate(out DateTime date)
                    && Satisfies(date.CompareTo(literal.AsDate()), op);
            case CellType.Text:
                return cell.TryGetText(out string? text)
                    && Satisfies(string.CompareOrdinal(text, literal.AsText()), op);
            case CellType.Boolean:
                if (op is not (QueryOperator.Equals or QueryOperator.NotEquals))
                    return false;
                return cell.TryGetBoolean(out bool flag)
                    && (op == QueryOperator.Equals ? flag == literal.AsBoolean() : flag != literal.AsBoolean());
            default:
                return false;
        }
    }

    private static bool Satisfies(int comparison, QueryOperator op) => op switch
    {
        QueryOperator.Equals => comparison == 0,
        QueryOperator.NotEquals => comparison != 0,
        QueryOperator.LessThan => comparison < 0,
        QueryOperator.LessThanOrEqual => comparison <= 0,
        QueryOperator.GreaterThan => comparison > 0,
        QueryOperator.GreaterThanOrEqual => comparison >= 0,
        _ => false,
    };
}
