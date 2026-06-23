namespace XLSight.Internal.Readers;

/// <summary>
/// A per-cursor column projection: cells in columns outside the projection keep their
/// position in the borrowed row (so windows and row presence are unchanged) but their
/// values are never materialized — no number parsing, no shared-string resolution.
/// </summary>
internal sealed class RowProjection
{
    private readonly ulong[] _mask = new ulong[(ExcelLimits.MaxColumns + 63) / 64];

    internal RowProjection(ReadOnlySpan<int> columns)
    {
        foreach (int column in columns)
        {
            if (column >= 1 && column <= ExcelLimits.MaxColumns)
            {
                _mask[(column - 1) >> 6] |= 1UL << ((column - 1) & 63);
            }
        }
    }

    internal bool IncludesColumn(int column) =>
        (uint)(column - 1) < ExcelLimits.MaxColumns
        && (_mask[(column - 1) >> 6] & (1UL << ((column - 1) & 63))) != 0;
}
