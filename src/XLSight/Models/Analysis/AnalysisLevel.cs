namespace XLSight.Models.Analysis;

/// <summary>Controls how much work XLSight performs when analyzing a workbook or sheet.</summary>
public enum AnalysisLevel : byte
{
    /// <summary>
    /// Return only exact metadata parsed from workbook and worksheet XML parts.
    /// Skips observed scan results and inferred heuristics.
    /// </summary>
    Exact = 0,

    /// <summary>
    /// Return exact metadata plus observed scan results from streaming the worksheet bytes.
    /// Skips inferred heuristics.
    /// </summary>
    Observed = 1,

    /// <summary>
    /// Return exact metadata, observed scan results, and inferred worksheet structure.
    /// This is the default and most comprehensive analysis mode.
    /// </summary>
    Full = 2,
}
