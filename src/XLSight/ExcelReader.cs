using XLSight.Models;

namespace XLSight;

/// <summary>
/// Provides convenience static methods for one-shot reading from an Excel file path
/// without manually managing an <see cref="ExcelWorkbook"/> lifetime.
/// </summary>
public static class ExcelReader
{
    /// <summary>Opens a workbook, reads a single cell, and closes it.</summary>
    /// <param name="filePath">Path to the .xlsx file.</param>
    /// <param name="sheet">The sheet name.</param>
    /// <param name="cellAddress">The cell address, e.g. "A1".</param>
    /// <param name="mode">Whether to return cached values or formula text.</param>
    /// <returns>The cell result containing its value and location.</returns>
    /// <exception cref="Exceptions.SheetNotFoundException">Thrown when the sheet does not exist.</exception>
    /// <exception cref="Exceptions.InvalidAddressException">Thrown when the address cannot be parsed.</exception>
    public static ExcelCellResult ReadCell(
        string filePath,
        string sheet,
        string cellAddress,
        ExcelReadMode mode = ExcelReadMode.Values)
    {
        using var workbook = ExcelWorkbook.Open(filePath);
        return workbook.ReadCell(sheet, cellAddress, mode);
    }

    /// <summary>Opens a workbook, reads a rectangular range of cells, and closes it.</summary>
    /// <param name="filePath">Path to the .xlsx file.</param>
    /// <param name="sheet">The sheet name.</param>
    /// <param name="rangeAddress">The range address, e.g. "A1:D10".</param>
    /// <param name="mode">Whether to return cached values or formula text.</param>
    /// <returns>The range result containing all cell values.</returns>
    /// <exception cref="Exceptions.SheetNotFoundException">Thrown when the sheet does not exist.</exception>
    /// <exception cref="Exceptions.InvalidAddressException">Thrown when the address cannot be parsed.</exception>
    /// <exception cref="Exceptions.RangeTooLargeException">Thrown when the range exceeds the cell limit.</exception>
    public static ExcelRangeResult ReadRange(
        string filePath,
        string sheet,
        string rangeAddress,
        ExcelReadMode mode = ExcelReadMode.Values)
    {
        using var workbook = ExcelWorkbook.Open(filePath);
        return workbook.ReadRange(sheet, rangeAddress, mode);
    }

    /// <summary>Opens a workbook asynchronously, reads a single cell, and closes it.</summary>
    /// <param name="filePath">Path to the .xlsx file.</param>
    /// <param name="sheet">The sheet name.</param>
    /// <param name="cellAddress">The cell address, e.g. "A1".</param>
    /// <param name="mode">Whether to return cached values or formula text.</param>
    /// <param name="ct">A cancellation token.</param>
    /// <returns>A task that returns the cell result.</returns>
    /// <exception cref="Exceptions.SheetNotFoundException">Thrown when the sheet does not exist.</exception>
    /// <exception cref="Exceptions.InvalidAddressException">Thrown when the address cannot be parsed.</exception>
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

    /// <summary>Opens a workbook asynchronously, reads a rectangular range of cells, and closes it.</summary>
    /// <param name="filePath">Path to the .xlsx file.</param>
    /// <param name="sheet">The sheet name.</param>
    /// <param name="rangeAddress">The range address, e.g. "A1:D10".</param>
    /// <param name="mode">Whether to return cached values or formula text.</param>
    /// <param name="ct">A cancellation token.</param>
    /// <returns>A task that returns the range result.</returns>
    /// <exception cref="Exceptions.SheetNotFoundException">Thrown when the sheet does not exist.</exception>
    /// <exception cref="Exceptions.InvalidAddressException">Thrown when the address cannot be parsed.</exception>
    /// <exception cref="Exceptions.RangeTooLargeException">Thrown when the range exceeds the cell limit.</exception>
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
