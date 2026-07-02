namespace XLSight.Analysis;

/// <summary>Heuristically inferred layout structure of a worksheet: axes, measure fields, and their groupings.</summary>
public sealed class SheetLayoutInfo
{
    internal static SheetLayoutInfo Empty { get; } = new()
    {
        Axes = [],
        MeasureFields = [],
        Groups = [],
    };

    /// <summary>Gets inferred layout axes that label measure fields.</summary>
    public required IReadOnlyList<LayoutAxis> Axes { get; init; }

    /// <summary>Gets inferred value-bearing measure fields.</summary>
    public required IReadOnlyList<MeasureFieldInfo> MeasureFields { get; init; }

    /// <summary>Gets groups tying together axes and the measure fields they label.</summary>
    public required IReadOnlyList<LayoutGroupInfo> Groups { get; init; }
}
