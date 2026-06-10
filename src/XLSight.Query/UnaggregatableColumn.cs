namespace XLSight.Query;

/// <summary>Reports cells skipped by an aggregate because they did not match its input type.</summary>
public sealed class UnaggregatableColumn
{
    /// <summary>Gets the source column name.</summary>
    public required string Column { get; init; }

    /// <summary>Gets the number of skipped cells.</summary>
    public required int SkippedCount { get; init; }

    /// <summary>Gets the 1-based sheet row indices of the first few skipped cells (at most 5).</summary>
    public required IReadOnlyList<int> SampleRowIndices { get; init; }
}
