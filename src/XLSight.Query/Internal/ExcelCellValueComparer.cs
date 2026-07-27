namespace XLSight.Query.Internal;

/// <summary>
/// A total order over <see cref="ExcelCellValue"/>, since the type is <see cref="IEquatable{T}"/>
/// only. Used to sort <c>ORDER BY</c> keys, which are real-world spreadsheet cells and therefore
/// span mixed types within one column.
/// </summary>
/// <remarks>
/// Two rules survive a direction flip. <see cref="CellType.Empty"/> always sorts last, never
/// "largest" the way SQL treats <c>NULL</c> — <c>ORDER BY SUM(x) DESC LIMIT 10</c> must not
/// surface ten groups whose column held no numeric data. And the cross-type rank from
/// <see cref="Rank"/> is fixed, so a stray text cell never outranks a real number just because the
/// query sorts <c>DESC</c>. Direction inverts only the same-type comparison.
/// </remarks>
internal sealed class ExcelCellValueComparer : IComparer<ExcelCellValue>
{
    public static readonly ExcelCellValueComparer Ascending = new(descending: false);
    public static readonly ExcelCellValueComparer Descending = new(descending: true);

    private readonly bool _descending;

    private ExcelCellValueComparer(bool descending)
    {
        _descending = descending;
    }

    public int Compare(ExcelCellValue x, ExcelCellValue y)
    {
        if (x.IsEmpty || y.IsEmpty)
        {
            if (x.IsEmpty && y.IsEmpty) { return 0; }
            return x.IsEmpty ? 1 : -1;
        }

        int rankX = Rank(x.CellType);
        int rankY = Rank(y.CellType);
        if (rankX != rankY)
        {
            return rankX.CompareTo(rankY);
        }

        int cmp = CompareSameRank(x, y);
        return _descending ? -cmp : cmp;
    }

    private static int CompareSameRank(ExcelCellValue x, ExcelCellValue y) => x.CellType switch
    {
        CellType.Number or CellType.Date => NumericValue(x).CompareTo(NumericValue(y)),
        CellType.Boolean => x.AsBoolean().CompareTo(y.AsBoolean()),
        CellType.Text => string.CompareOrdinal(x.AsText(), y.AsText()),
        _ => string.CompareOrdinal(x.ToString(), y.ToString()),
    };

    /// <summary>Number/Date first, then Boolean, then Text, then Error/Formula.</summary>
    private static int Rank(CellType type) => type switch
    {
        CellType.Number or CellType.Date => 0,
        CellType.Boolean => 1,
        CellType.Text => 2,
        _ => 3, // Error, Formula
    };

    private static double NumericValue(ExcelCellValue value) =>
        value.CellType == CellType.Date ? value.AsDate().Ticks : value.AsNumber();
}
