namespace XLSight.Query;

/// <summary>The aggregate function kinds supported by <see cref="SheetQuery.Select"/>.</summary>
public enum AggregateKind
{
    /// <summary>Sum of numeric cells.</summary>
    Sum,

    /// <summary>Count of rows matching the filters.</summary>
    Count,

    /// <summary>Minimum of numeric or date cells.</summary>
    Min,

    /// <summary>Maximum of numeric or date cells.</summary>
    Max,

    /// <summary>Arithmetic mean of numeric cells.</summary>
    Average,
}
