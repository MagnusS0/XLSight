using System.Runtime.InteropServices;

namespace XLSight.Models.Analysis;

/// <summary>Represents a merged cell region in an Excel worksheet.</summary>
[StructLayout(LayoutKind.Auto)]
public readonly record struct ExcelMergedRegion(
    int StartRow,
    int StartColumn,
    int EndRow,
    int EndColumn);
