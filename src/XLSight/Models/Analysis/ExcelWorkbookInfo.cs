namespace XLSight.Models.Analysis;

public sealed class ExcelWorkbookInfo
{
    public required IReadOnlyList<ExcelSheetInfo> Sheets { get; init; }
    public required IReadOnlyList<ExcelNamedRange> NamedRanges { get; init; }
    public required bool HasMacros { get; init; }
    public required bool IsDate1904 { get; init; }
    public required DateTimeOffset AnalyzedAtUtc { get; init; }
}
