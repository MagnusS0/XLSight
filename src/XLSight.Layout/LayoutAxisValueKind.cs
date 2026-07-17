namespace XLSight.Layout;

/// <summary>Describes the dominant value kind carried by a layout axis.</summary>
public enum LayoutAxisValueKind
{
    /// <summary>The axis mostly contains text labels.</summary>
    Text = 0,

    /// <summary>The axis mostly contains numeric labels.</summary>
    Numeric = 1,

    /// <summary>The axis mostly contains date labels.</summary>
    Date = 2,

    /// <summary>The axis contains a material mix of label kinds.</summary>
    Mixed = 3,
}
