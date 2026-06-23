namespace XLSight.Query;

/// <summary>Describes a single aggregate to compute: a function and, except for Count, a source column.</summary>
public readonly record struct AggregateSpec
{
    internal AggregateSpec(AggregateKind kind, string? column)
    {
        Kind = kind;
        Column = column;
    }

    /// <summary>Gets the aggregate function kind.</summary>
    public AggregateKind Kind { get; }

    /// <summary>Gets the source column name, or null for <see cref="AggregateKind.Count"/>.</summary>
    public string? Column { get; }

    /// <summary>Gets the result column label, e.g. <c>Sum(NetSales)</c> or <c>Count()</c>.</summary>
    public string Label => Column is null ? $"{Kind}()" : $"{Kind}({Column})";
}
