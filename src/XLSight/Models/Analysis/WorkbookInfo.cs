namespace XLSight.Analysis;

/// <summary>Describes the structural properties of an entire Excel workbook after analysis.</summary>
public sealed class WorkbookInfo
{
    /// <summary>Gets the analysis level used to produce this result.</summary>
    public required AnalysisLevel Level { get; init; }

    /// <summary>Gets the structural profiles for all sheets in the workbook.</summary>
    public required IReadOnlyList<SheetInfo> Sheets { get; init; }

    /// <summary>Gets exact workbook facts parsed from workbook metadata and related parts.</summary>
    public required WorkbookAnalysisExact Exact { get; init; }

    /// <summary>Gets all named ranges defined in the workbook.</summary>
    public IReadOnlyList<NamedRange> NamedRanges => Exact.NamedRanges;

    /// <summary>Gets all structured tables defined in the workbook.</summary>
    public IReadOnlyList<TableInfo> Tables => Exact.Tables;

    /// <summary>Gets all pivot tables defined in the workbook.</summary>
    public IReadOnlyList<PivotTableInfo> PivotTables => Exact.PivotTables;

    /// <summary>Gets all charts defined in the workbook.</summary>
    public IReadOnlyList<ChartInfo> Charts => Exact.Charts;

    /// <summary>Gets a value indicating whether the workbook contains VBA macros.</summary>
    public bool HasMacros => Exact.HasMacros;

    /// <summary>Gets a value indicating whether the workbook uses the 1904 date system.</summary>
    public bool IsDate1904 => Exact.IsDate1904;

    /// <summary>Gets a value indicating whether observed sheet scan data is available.</summary>
    public bool HasObserved => Level >= AnalysisLevel.Observed;

    /// <summary>Gets a value indicating whether inferred sheet structure is available.</summary>
    public bool HasInferred => Level >= AnalysisLevel.Full;

    /// <summary>Gets the UTC timestamp at which this analysis was performed.</summary>
    public required DateTimeOffset AnalyzedAtUtc { get; init; }
}
