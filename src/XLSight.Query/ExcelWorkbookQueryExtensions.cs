namespace XLSight.Query;

/// <summary>Query entry points over <see cref="ExcelWorkbook"/>.</summary>
public static class ExcelWorkbookQueryExtensions
{
    /// <summary>
    /// Starts a streaming query over a sheet range. Column names are taken from the header row
    /// (by default the first non-empty row of the range), then filtered, grouped, and aggregated
    /// in a single pass with bounded memory.
    /// </summary>
    /// <param name="workbook">The open workbook.</param>
    /// <param name="sheet">The sheet name.</param>
    /// <param name="range">The range address, e.g. "A6:F2410". Case-insensitive.</param>
    /// <param name="headerRow">
    /// The 1-based sheet row containing the column headers, or 0 to use the first
    /// non-empty row of the range. Data rows start after the header row.
    /// </param>
    /// <returns>A query builder.</returns>
    /// <exception cref="ArgumentNullException">Thrown when an argument is null.</exception>
    /// <exception cref="InvalidAddressException">Thrown when the range cannot be parsed.</exception>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="headerRow"/> is negative or outside the range.</exception>
    public static SheetQuery QueryRange(this ExcelWorkbook workbook, string sheet, string range, int headerRow = 0)
    {
        ArgumentNullException.ThrowIfNull(range);
        return QueryRange(workbook, sheet, ExcelRange.Parse(range), headerRow);
    }

    /// <summary>
    /// Starts a streaming query over a typed sheet range. Column names are taken from the header
    /// row (by default the first non-empty row of the range), then filtered, grouped, and
    /// aggregated in a single pass with bounded memory.
    /// </summary>
    /// <param name="workbook">The open workbook.</param>
    /// <param name="sheet">The sheet name.</param>
    /// <param name="range">The range to query.</param>
    /// <param name="headerRow">
    /// The 1-based sheet row containing the column headers, or 0 to use the first
    /// non-empty row of the range. Data rows start after the header row.
    /// </param>
    /// <returns>A query builder.</returns>
    /// <exception cref="ArgumentNullException">Thrown when an argument is null.</exception>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="headerRow"/> is negative or outside the range.</exception>
    public static SheetQuery QueryRange(this ExcelWorkbook workbook, string sheet, ExcelRange range, int headerRow = 0)
    {
        ArgumentNullException.ThrowIfNull(workbook);
        ArgumentNullException.ThrowIfNull(sheet);
        ArgumentOutOfRangeException.ThrowIfNegative(headerRow);
        if (headerRow > 0 && !range.IsUnbounded
            && (headerRow < range.TopLeft.Row || headerRow > range.BottomRight.Row))
        {
            throw new ArgumentOutOfRangeException(
                nameof(headerRow), headerRow, $"Header row must lie within the queried range rows {range.TopLeft.Row}-{range.BottomRight.Row}.");
        }

        return new SheetQuery(workbook, sheet, range, headerRow);
    }
}
