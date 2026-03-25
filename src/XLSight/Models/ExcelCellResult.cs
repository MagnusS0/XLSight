namespace XLSight.Models;

public sealed class ExcelCellResult
{
    public required string Sheet { get; init; }
    public required int Row { get; init; }      // 1-based
    public required int Column { get; init; }   // 1-based
    public required ExcelCellValue Value { get; init; }
}
