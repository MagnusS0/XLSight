namespace XLSight.Analysis;

/// <summary>Describes how a layout axis participates in a measure field.</summary>
public enum LayoutAxisRole
{
    /// <summary>The axis is a primary row or column coordinate.</summary>
    Primary = 0,

    /// <summary>The axis provides repeated context such as unit, scenario, or qualifier values.</summary>
    Context = 1,
}
