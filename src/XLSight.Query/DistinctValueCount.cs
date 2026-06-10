namespace XLSight.Query;

/// <summary>A distinct cell value and its occurrence count, returned by <see cref="SheetQuery.DistinctValues"/>.</summary>
/// <param name="Value">The display string of the value (invariant formatting).</param>
/// <param name="Count">The number of matching rows containing the value.</param>
public readonly record struct DistinctValueCount(string Value, int Count);
