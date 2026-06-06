namespace XLSight.Analysis;

/// <summary>Represents an inferred header band in a worksheet.</summary>
public sealed class HeaderBandInfo
{
    /// <summary>Gets the header band range.</summary>
    public required ExcelRange Range { get; init; }

    /// <summary>Gets the 1-based row indices covered by the band.</summary>
    public required IReadOnlyList<int> Rows { get; init; }

}
