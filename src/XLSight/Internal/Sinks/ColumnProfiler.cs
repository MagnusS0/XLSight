using XLSight.Models;
using XLSight.Models.Analysis;

namespace XLSight.Internal.Sinks;

internal static class ColumnProfiler
{
    /// <summary>
    /// Builds a list of <see cref="ColumnProfile"/> from accumulated per-column state,
    /// ordered by column index ascending.
    /// </summary>
    internal static IReadOnlyList<ColumnProfile> BuildProfiles(
        Dictionary<int, ColumnState> columnStates,
        Dictionary<int, string> headersByColumn)
    {
        if (columnStates.Count == 0)
        {
            return [];
        }

        var profiles = new List<ColumnProfile>(columnStates.Count);
        foreach (var (col, state) in columnStates.OrderBy(kv => kv.Key))
        {
            headersByColumn.TryGetValue(col, out string? header);
            profiles.Add(BuildProfile(col, state, header));
        }

        return profiles;
    }

    private static ColumnProfile BuildProfile(int columnIndex, ColumnState state, string? header)
    {
        var dominantType = ResolveDominantType(state);

        double? minNum = state.HasNumeric ? state.MinNumeric : null;
        double? maxNum = state.HasNumeric ? state.MaxNumeric : null;
        int? maxText = state.MaxTextLength > 0 ? state.MaxTextLength : null;

        return new ColumnProfile
        {
            ColumnIndex = columnIndex,
            InferredHeader = header,
            DominantType = dominantType,
            NonEmptyCount = state.NonEmptyCount,
            DistinctValueEstimate = state.DistinctValues?.Count ?? state.DistinctEstimate,
            MinNumericValue = minNum,
            MaxNumericValue = maxNum,
            MaxTextLength = maxText,
            HasFormulas = state.HasFormulas,
        };
    }

    private static CellType ResolveDominantType(ColumnState state)
    {
        if (state.NonEmptyCount == 0)
        {
            return CellType.Empty;
        }

        CellType best = CellType.Empty;
        int bestCount = 0;

        Check(CellType.Number, state.NumberCount, ref best, ref bestCount);
        Check(CellType.Text, state.TextCount, ref best, ref bestCount);
        Check(CellType.Date, state.DateCount, ref best, ref bestCount);
        Check(CellType.Boolean, state.BooleanCount, ref best, ref bestCount);
        Check(CellType.Error, state.ErrorCount, ref best, ref bestCount);

        return best;
    }

    private static void Check(CellType type, int count, ref CellType best, ref int bestCount)
    {
        if (count > bestCount)
        {
            best = type;
            bestCount = count;
        }
    }
}
