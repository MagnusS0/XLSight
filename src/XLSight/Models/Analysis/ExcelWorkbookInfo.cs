namespace XLSight.Models.Analysis;

/// <summary>Describes the structural properties of an entire Excel workbook after analysis.</summary>
public sealed class ExcelWorkbookInfo
{
    /// <summary>Gets the structural profiles for all sheets in the workbook.</summary>
    public required IReadOnlyList<ExcelSheetInfo> Sheets { get; init; }

    /// <summary>Gets all named ranges defined in the workbook.</summary>
    public required IReadOnlyList<ExcelNamedRange> NamedRanges { get; init; }

    /// <summary>Gets a value indicating whether the workbook contains VBA macros.</summary>
    public required bool HasMacros { get; init; }

    /// <summary>Gets a value indicating whether the workbook uses the 1904 date system.</summary>
    public required bool IsDate1904 { get; init; }

    /// <summary>Gets the UTC timestamp at which this analysis was performed.</summary>
    public required DateTimeOffset AnalyzedAtUtc { get; init; }
}
