namespace XLSight.Models.Analysis;

public sealed class ExcelTableInfo
{
    public required string Name { get; init; }
    public required string Sheet { get; init; }
    public required ExcelRange Range { get; init; }
    public required IReadOnlyList<string> ColumnNames { get; init; }
}
