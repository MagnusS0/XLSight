namespace XLSight.Models.Analysis;

public sealed class ExcelColumnProfile
{
    public required int ColumnIndex { get; init; }           // 1-based
    public required string? InferredHeader { get; init; }
    public required ExcelCellType DominantType { get; init; }
    public required int NonEmptyCount { get; init; }
    public required int DistinctValueEstimate { get; init; }
    public required double? MinNumericValue { get; init; }
    public required double? MaxNumericValue { get; init; }
    public required int? MaxTextLength { get; init; }
    public required bool HasFormulas { get; init; }
}
