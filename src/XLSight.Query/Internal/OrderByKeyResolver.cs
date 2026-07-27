namespace XLSight.Query.Internal;

/// <summary>
/// Resolves an <c>ORDER BY</c> key against a grouped query's result shape to an index into the
/// result columns: <c>0</c> for the group key, <c>i + 1</c> for aggregate <c>i</c>. Shared by the
/// DSL parser and <see cref="SheetQuery"/>'s fluent <c>OrderBy</c> so both paths apply the same
/// rule: a plain identifier matches the group column, a parenthesized aggregate call matches
/// structurally on <c>(Kind, Column)</c> — never on <see cref="AggregateSpec.Label"/> text, which
/// is derived from the enum name and would silently reject DSL spellings such as <c>AVG</c>.
/// </summary>
internal static class OrderByKeyResolver
{
    /// <summary>Resolves the key to a result-column index, or -1 when it matches neither.</summary>
    public static int Resolve(
        string? groupBy,
        IReadOnlyList<AggregateSpec> aggregates,
        string? column,
        AggregateKind? aggregateKind)
    {
        if (aggregateKind is null)
        {
            return column is not null
                && groupBy is not null
                && string.Equals(column, groupBy, StringComparison.OrdinalIgnoreCase)
                ? 0
                : -1;
        }

        for (int i = 0; i < aggregates.Count; i++)
        {
            AggregateSpec spec = aggregates[i];
            if (spec.Kind == aggregateKind.Value && string.Equals(spec.Column, column, StringComparison.OrdinalIgnoreCase))
            {
                return i + 1;
            }
        }

        return -1;
    }

    /// <summary>Describes the valid keys for an error message: the group column, then each aggregate's label.</summary>
    public static string DescribeValidKeys(string? groupBy, IReadOnlyList<AggregateSpec> aggregates)
    {
        var keys = new List<string>(aggregates.Count + 1);
        if (groupBy is not null) { keys.Add(groupBy); }
        foreach (AggregateSpec spec in aggregates) { keys.Add(spec.Label); }
        return string.Join(", ", keys);
    }
}
