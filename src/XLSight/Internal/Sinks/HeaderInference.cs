
namespace XLSight.Internal.Sinks;

internal static class HeaderInference
{
    /// <summary>
    /// Returns the 1-based row index of the inferred header row, or 0 if no header is detected.
    /// A header is inferred when: there is more than one data row total, and all non-empty cells
    /// in the first row are of type <see cref="CellType.Text"/>.
    /// </summary>
    internal static int Infer(
        int firstRowIndex,
        ReadOnlySpan<ExcelCellValue> firstRowCells,
        int totalRowCount)
    {
        if (totalRowCount <= 1 || firstRowCells.IsEmpty)
        {
            return 0;
        }

        foreach (var cell in firstRowCells)
        {
            if (!cell.IsEmpty && cell.CellType != CellType.Text)
            {
                return 0;
            }
        }

        return firstRowIndex;
    }
}
