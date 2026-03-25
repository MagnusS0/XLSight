using XLSight.Models;

namespace XLSight;

public static class ExcelReader
{
    public static ExcelCellResult ReadCell(
        string filePath,
        string sheet,
        string cellAddress,
        ExcelReadMode mode = ExcelReadMode.Values)
    {
        using var workbook = ExcelWorkbook.Open(filePath);
        return workbook.ReadCell(sheet, cellAddress, mode);
    }

    public static ExcelRangeResult ReadRange(
        string filePath,
        string sheet,
        string rangeAddress,
        ExcelReadMode mode = ExcelReadMode.Values)
    {
        using var workbook = ExcelWorkbook.Open(filePath);
        return workbook.ReadRange(sheet, rangeAddress, mode);
    }

    public static async Task<ExcelCellResult> ReadCellAsync(
        string filePath,
        string sheet,
        string cellAddress,
        ExcelReadMode mode = ExcelReadMode.Values,
        CancellationToken ct = default)
    {
        var workbook = await ExcelWorkbook.OpenAsync(filePath, ct).ConfigureAwait(false);
        await using (workbook.ConfigureAwait(false))
        {
            return await workbook.ReadCellAsync(sheet, cellAddress, mode, ct).ConfigureAwait(false);
        }
    }

    public static async Task<ExcelRangeResult> ReadRangeAsync(
        string filePath,
        string sheet,
        string rangeAddress,
        ExcelReadMode mode = ExcelReadMode.Values,
        CancellationToken ct = default)
    {
        var workbook = await ExcelWorkbook.OpenAsync(filePath, ct).ConfigureAwait(false);
        await using (workbook.ConfigureAwait(false))
        {
            return await workbook.ReadRangeAsync(sheet, rangeAddress, mode, ct).ConfigureAwait(false);
        }
    }
}
