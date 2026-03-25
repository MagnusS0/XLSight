namespace XLSight.Models.Analysis;

/// <summary>Represents a merged cell region in an Excel worksheet.</summary>
public sealed record ExcelMergedRegion(ExcelAddress TopLeft, ExcelAddress BottomRight);
