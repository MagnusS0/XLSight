namespace XLSight;

internal static class ExcelLimits
{
    public const int MaxRows = 1_048_576;
    public const int MaxColumns = 16_384;
    public const long MaxCells = 100_000_000;
    public const int MaxSharedStringCount = 10_000_000;
    public const int MaxStyleCount = 100_000;
    public const int MaxMergedRegions = 1_000_000;
    public const int MaxNamedRanges = 100_000;
}
