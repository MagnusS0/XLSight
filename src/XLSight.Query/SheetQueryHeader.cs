namespace XLSight.Query;

/// <summary>Describes the Query DSL <c>HEADER</c> clause.</summary>
public readonly record struct SheetQueryHeader
{
    private SheetQueryHeader(SheetQueryHeaderKind kind, int row, string? column)
    {
        Kind = kind;
        Row = row;
        Column = column;
    }

    /// <summary>Gets the header discovery mode.</summary>
    public SheetQueryHeaderKind Kind { get; }

    /// <summary>Gets the 1-based sheet row for <see cref="SheetQueryHeaderKind.Row"/> headers.</summary>
    public int Row { get; }

    /// <summary>Gets the sheet column for <see cref="SheetQueryHeaderKind.Column"/> headers.</summary>
    public string? Column { get; }

    /// <summary>Creates an automatic header specification.</summary>
    public static SheetQueryHeader Auto() => new(SheetQueryHeaderKind.Auto, 0, null);

    /// <summary>Creates an explicit row header specification.</summary>
    /// <param name="row">The 1-based sheet row containing headers.</param>
    public static SheetQueryHeader FromRow(int row)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(row);
        return new(SheetQueryHeaderKind.Row, row, null);
    }

    /// <summary>Creates an explicit column header specification for a transposed table.</summary>
    /// <param name="column">The Excel column containing headers.</param>
    public static SheetQueryHeader FromColumn(string column)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(column);
        return new(SheetQueryHeaderKind.Column, 0, column);
    }
}
