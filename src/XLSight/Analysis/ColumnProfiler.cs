using XLSight.Models;
using XLSight.Models.Analysis;
using XLSight.Worksheets;

namespace XLSight.Analysis;

internal static class ColumnProfiler
{
    /// <summary>
    /// Builds a list of <see cref="ExcelColumnProfile"/> from accumulated per-column state,
    /// ordered by column index ascending.
    /// </summary>
    internal static IReadOnlyList<ExcelColumnProfile> BuildProfiles(
        Dictionary<int, ColumnState> columnStates,
        Dictionary<int, string> headersByColumn)
    {
        if (columnStates.Count == 0)
        {
            return [];
        }

        var profiles = new List<ExcelColumnProfile>(columnStates.Count);
        foreach (var (col, state) in columnStates.OrderBy(kv => kv.Key))
        {
            headersByColumn.TryGetValue(col, out string? header);
            profiles.Add(BuildProfile(col, state, header));
        }

        return profiles;
    }

    private static ExcelColumnProfile BuildProfile(int columnIndex, ColumnState state, string? header)
    {
        var dominantType = ResolveDominantType(state);

        double? minNum = state.HasNumeric ? state.MinNumeric : null;
        double? maxNum = state.HasNumeric ? state.MaxNumeric : null;
        int? maxText = state.MaxTextLength > 0 ? state.MaxTextLength : null;

        return new ExcelColumnProfile
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

    private static ExcelCellType ResolveDominantType(ColumnState state)
    {
        if (state.NonEmptyCount == 0)
        {
            return ExcelCellType.Empty;
        }

        ExcelCellType best = ExcelCellType.Empty;
        int bestCount = 0;

        Check(ExcelCellType.Number, state.NumberCount, ref best, ref bestCount);
        Check(ExcelCellType.Text, state.TextCount, ref best, ref bestCount);
        Check(ExcelCellType.Date, state.DateCount, ref best, ref bestCount);
        Check(ExcelCellType.Boolean, state.BooleanCount, ref best, ref bestCount);
        Check(ExcelCellType.Error, state.ErrorCount, ref best, ref bestCount);

        return best;
    }

    private static void Check(ExcelCellType type, int count, ref ExcelCellType best, ref int bestCount)
    {
        if (count > bestCount)
        {
            best = type;
            bestCount = count;
        }
    }
}
