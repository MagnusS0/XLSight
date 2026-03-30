namespace XLSight.Models.Analysis;

/// <summary>Represents an inferred header band in a worksheet.</summary>
public sealed class HeaderBandInfo
{
    /// <summary>Gets the header band range.</summary>
    public required ExcelRange Range { get; init; }

    /// <summary>Gets the 1-based row indices covered by the band.</summary>
    public required IReadOnlyList<int> Rows { get; init; }

    /// <summary>Gets a coarse confidence score from 0 to 1 for the inference.</summary>
    public required double Confidence { get; init; }
}
