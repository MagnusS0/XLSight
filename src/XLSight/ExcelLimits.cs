namespace XLSight;

/// <summary>Excel specification limits used throughout the library.</summary>
public static class ExcelLimits
{
    /// <summary>Maximum number of rows in an Excel worksheet (1,048,576).</summary>
    public const int MaxRows = 1_048_576;

    /// <summary>Maximum number of columns in an Excel worksheet (16,384 = XFD).</summary>
    public const int MaxColumns = 16_384;

    /// <summary>Maximum number of cells that can be read in a single range operation.</summary>
    public const long MaxCells = 100_000_000;

    /// <summary>Maximum number of shared string table entries.</summary>
    public const int MaxSharedStringCount = 10_000_000;

    /// <summary>Maximum number of cell styles.</summary>
    public const int MaxStyleCount = 100_000;

    /// <summary>Maximum number of merged regions in a worksheet.</summary>
    public const int MaxMergedRegions = 1_000_000;

    /// <summary>Maximum number of named ranges in a workbook.</summary>
    public const int MaxNamedRanges = 100_000;
}
