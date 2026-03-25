namespace XLSight.Models.Analysis;

public sealed class ExcelNamedRange
{
    public required string Name { get; init; }
    public required string? Sheet { get; init; }        // null for workbook-scoped
    public required string Reference { get; init; }     // raw reference string, e.g. "Sheet1!$A$1:$D$100"
}
