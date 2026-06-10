namespace XLSight.Query.Internal;

internal static class FilterEvaluator
{
    /// <summary>
    /// Strictly typed comparison: only cells of the literal's type can satisfy a predicate
    /// (a text cell never matches a numeric literal, not even for NotEquals), and empty
    /// cells never match. Text ordering is ordinal.
    /// </summary>
    public static bool Matches(in ExcelCellValue cell, QueryOp op, in ExcelCellValue literal)
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
                return cell.TryGetBoolean(out bool flag)
                    && (op == QueryOp.Equals ? flag == literal.AsBoolean() : flag != literal.AsBoolean());
            default:
                return false;
        }
    }

    private static bool Satisfies(int comparison, QueryOp op) => op switch
    {
        QueryOp.Equals => comparison == 0,
        QueryOp.NotEquals => comparison != 0,
        QueryOp.LessThan => comparison < 0,
        QueryOp.LessThanOrEqual => comparison <= 0,
        QueryOp.GreaterThan => comparison > 0,
        QueryOp.GreaterThanOrEqual => comparison >= 0,
        _ => false,
    };
}
