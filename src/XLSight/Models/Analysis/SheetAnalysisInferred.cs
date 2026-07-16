namespace XLSight.Analysis;

/// <summary>Inferred worksheet structure derived from exact metadata and observed scan facts.</summary>
public sealed class SheetAnalysisInferred
{
    internal static SheetAnalysisInferred Empty { get; } = new()
    {
        Regions = [],
        HeaderBands = [],
        HeaderRowIndex = 0,
        Warnings = [],
    };

    /// <summary>Gets inferred structural regions for the sheet.</summary>
    public required IReadOnlyList<RegionInfo> Regions { get; init; }

    /// <summary>Gets inferred header bands.</summary>
    public required IReadOnlyList<HeaderBandInfo> HeaderBands { get; init; }

    /// <summary>Gets the 1-based row index inferred as the primary header row, or 0 if none could be inferred.</summary>
    public required int HeaderRowIndex { get; init; }

    /// <summary>Gets worksheet-level analysis warnings.</summary>
    public required IReadOnlyList<AnalysisWarning> Warnings { get; init; }
}
