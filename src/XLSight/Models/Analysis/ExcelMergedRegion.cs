using System.Runtime.InteropServices;

namespace XLSight.Models.Analysis;

/// <summary>Represents a merged cell region in an Excel worksheet.</summary>
/// <param name="StartRow">The 1-based row index of the top edge of the merged region.</param>
/// <param name="StartColumn">The 1-based column index of the left edge of the merged region.</param>
/// <param name="EndRow">The 1-based row index of the bottom edge of the merged region.</param>
/// <param name="EndColumn">The 1-based column index of the right edge of the merged region.</param>
[StructLayout(LayoutKind.Auto)]
public readonly record struct ExcelMergedRegion(
    int StartRow,
    int StartColumn,
    int EndRow,
    int EndColumn);
