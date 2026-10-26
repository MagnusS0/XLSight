namespace XLSight.FastBench;

internal static class Stats
{
    public static double Median(IReadOnlyList<double> values)
    {
        if (values.Count == 0)
        {
            return double.NaN;
        }

        double[] sorted = [.. values];
        Array.Sort(sorted);
        int mid = sorted.Length / 2;
        return sorted.Length % 2 == 1 ? sorted[mid] : (sorted[mid - 1] + sorted[mid]) / 2;
    }

    /// <summary>Median absolute deviation from the median (unscaled).</summary>
    public static double Mad(IReadOnlyList<double> values)
    {
        double median = Median(values);
        return Median([.. values.Select(v => Math.Abs(v - median))]);
    }
}
