namespace XLSight.Exceptions;

/// <summary>Thrown when a requested sheet name does not exist in the workbook.</summary>
public sealed class SheetNotFoundException : ExcelException
{
    /// <summary>The sheet name that was not found.</summary>
    public string SheetName { get; }

    public SheetNotFoundException(string sheetName)
        : base($"Sheet '{sheetName}' was not found in the workbook.")
        => SheetName = sheetName;
}
