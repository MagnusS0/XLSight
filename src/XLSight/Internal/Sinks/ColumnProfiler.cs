using System.Buffers;
using XLSight.Analysis;

namespace XLSight.Internal.Sinks;

internal static class ColumnProfiler
{
    /// <summary>
    /// Builds a list of <see cref="ColumnProfile"/> from accumulated per-column state,
    /// ordered by column index ascending.
    /// </summary>
    internal static IReadOnlyList<ColumnProfile> BuildProfiles(
        Dictionary<int, ColumnState> columnStates,
        Dictionary<int, string> headersByColumn,
        ISharedStringSource sst,
        int distinctValuesCap,
        Dictionary<int, int>? formulaCountsByColumn = null)
    {
        int count = columnStates.Count;
        if (count == 0)
        {
            return [];
        }

        // Sort column keys without LINQ: rent an int[], sort it, build profiles in order.
        int[] keys = ArrayPool<int>.Shared.Rent(count);
        int idx = 0;
        foreach (int k in columnStates.Keys) { keys[idx++] = k; }
        Array.Sort(keys, 0, count);

        var profiles = new List<ColumnProfile>(count);
        for (int i = 0; i < count; i++)
        {
            int col = keys[i];
            headersByColumn.TryGetValue(col, out string? header);
            bool hasFormulas = formulaCountsByColumn?.ContainsKey(col) ?? false;
            profiles.Add(BuildProfile(col, columnStates[col], header, hasFormulas, sst, distinctValuesCap));
        }

        ArrayPool<int>.Shared.Return(keys);
        return profiles;
    }

    private static ColumnProfile BuildProfile(
        int columnIndex,
        ColumnState state,
        string? header,
        bool hasFormulas,
        ISharedStringSource sst,
        int distinctValuesCap)
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
            TextCount = state.TextCount,
            NumberCount = state.NumberCount,
            DateCount = state.DateCount,
            BooleanCount = state.BooleanCount,
            DistinctValueEstimate = state.DistinctCount,
            DistinctValues = state.BuildDistinctValues(distinctValuesCap, sst),
            MinNumericValue = minNum,
            MaxNumericValue = maxNum,
            MaxTextLength = maxText,
            HasFormulas = hasFormulas,
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
