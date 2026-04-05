namespace XLSight;

internal static class ExcelLimits
{
    internal const int MaxRows = 1_048_576;
    internal const int MaxColumns = 16_384;
    internal const long MaxCells = 100_000_000;
    internal const int MaxSharedStringCount = 10_000_000;
    internal const int MaxStyleCount = 100_000;
    internal const int MaxMergedRegions = 1_000_000;
    internal const int MaxNamedRanges = 100_000;
}
