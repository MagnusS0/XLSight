namespace XLSight.Analysis;

/// <summary>Exact workbook facts parsed from package parts and workbook metadata.</summary>
public sealed class WorkbookAnalysisExact
{
    /// <summary>Gets the named ranges or formulas defined in the workbook.</summary>
    public required IReadOnlyList<NamedRange> NamedRanges { get; init; }

    /// <summary>Gets all structured tables across the workbook.</summary>
    public required IReadOnlyList<TableInfo> Tables { get; init; }

    /// <summary>Gets all pivot tables across the workbook.</summary>
    public required IReadOnlyList<PivotTableInfo> PivotTables { get; init; }

    /// <summary>Gets all charts across the workbook.</summary>
    public required IReadOnlyList<ChartInfo> Charts { get; init; }

    /// <summary>Gets a value indicating whether the workbook contains VBA macros.</summary>
    public required bool HasMacros { get; init; }

    /// <summary>Gets a value indicating whether the workbook uses the 1904 date system.</summary>
    public required bool IsDate1904 { get; init; }

    /// <summary>Gets workbook-level analysis warnings.</summary>
    public required IReadOnlyList<AnalysisWarning> Warnings { get; init; }
}
