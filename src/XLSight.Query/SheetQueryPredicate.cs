namespace XLSight.Query;

/// <summary>Describes one Query DSL <c>WHERE</c> predicate.</summary>
public readonly record struct SheetQueryPredicate
{
    /// <summary>Creates a parsed Query DSL predicate.</summary>
    /// <param name="column">The column name from the header row.</param>
    /// <param name="op">The comparison operator.</param>
    /// <param name="literal">The typed literal value.</param>
    public SheetQueryPredicate(string column, QueryOp op, ExcelCellValue literal)
    {
        Column = column;
        Op = op;
        Literal = literal;
    }

    /// <summary>Gets the column name from the header row.</summary>
    public string Column { get; }

    /// <summary>Gets the comparison operator.</summary>
    public QueryOp Op { get; }

    /// <summary>Gets the typed literal value.</summary>
    public ExcelCellValue Literal { get; }
}
