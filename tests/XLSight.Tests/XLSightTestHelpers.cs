namespace XLSight.Tests;

internal static class XLSightTestHelpers
{
    internal static bool RowHasValue(ExcelRow row)
    {
        foreach (ExcelCellValue cell in row)
        {
            if (cell.HasValue)
            {
                return true;
            }
        }

        return false;
    }
}
