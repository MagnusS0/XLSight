namespace XLSight.Analysis;

/// <summary>Options controlling sheet analysis behavior.</summary>
public sealed class AnalysisOptions
{
    /// <summary>Gets the shared default options instance.</summary>
    public static AnalysisOptions Default { get; } = new();

    /// <summary>
    /// Gets the maximum number of distinct values surfaced in <see cref="ColumnProfile.DistinctValues"/>.
    /// Columns whose distinct count exceeds this cap report only
    /// <see cref="ColumnProfile.DistinctValueEstimate"/>. Set to 0 to disable
    /// distinct-value materialization entirely. Defaults to 32.
    /// </summary>
    public int DistinctValuesCap { get; init; } = 32;
}
