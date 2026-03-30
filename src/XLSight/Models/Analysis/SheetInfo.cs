namespace XLSight.Models.Analysis;

/// <summary>Describes the structural properties of a single Excel worksheet after analysis.</summary>
public sealed class SheetInfo
{
    private string[]? _formulaColumns;

    /// <summary>Gets the analysis level used to produce this result.</summary>
    public required AnalysisLevel Level { get; init; }

    /// <summary>Gets the name of the sheet.</summary>
    public required string SheetName { get; init; }

    /// <summary>Gets the 0-based index of this sheet within the workbook.</summary>
    public required int SheetIndex { get; init; }

    /// <summary>Gets exact worksheet facts parsed from the package.</summary>
    public required SheetAnalysisExact Exact { get; init; }

    /// <summary>Gets worksheet facts observed during the streaming value scan.</summary>
    public required SheetAnalysisObserved Observed { get; init; }

    /// <summary>Gets inferred worksheet structure derived from exact and observed facts.</summary>
    public required SheetAnalysisInferred Inferred { get; init; }

    /// <summary>Gets a value indicating whether observed scan data is available for this sheet.</summary>
    public bool HasObserved => Level >= AnalysisLevel.Observed;

    /// <summary>Gets a value indicating whether inferred structure is available for this sheet.</summary>
    public bool HasInferred => Level >= AnalysisLevel.Full;

    /// <summary>Gets the bounding range of all non-empty cells, or null if the sheet is empty.</summary>
    public ExcelRange? UsedRange => RequireObserved().ValueUsedRange;

    /// <summary>Gets the number of non-empty rows in the used range.</summary>
    public int RowCount => RequireObserved().RowCount;

    /// <summary>Gets the number of non-empty columns in the used range.</summary>
    public int ColumnCount => RequireObserved().ColumnCount;

    /// <summary>Gets the total number of non-empty cells in the sheet.</summary>
    public int CellCount => RequireObserved().CellCount;

    /// <summary>Gets the column-level profiles for each column that contains data.</summary>
    public IReadOnlyList<ColumnProfile> Columns => RequireObserved().Columns;

    /// <summary>Gets the Excel-style column letters (e.g. "A", "BC") of columns that contain formulas.</summary>
    public IReadOnlyList<string> FormulaColumns => _formulaColumns ??= [.. RequireObserved().FormulaColumns.Select(c => c.ColumnLabel)];

    /// <summary>Gets all merged cell regions in this sheet.</summary>
    public IReadOnlyList<MergedRegion> MergedRegions => Exact.MergedRegions;

    /// <summary>Gets the structured tables defined in this sheet.</summary>
    public IReadOnlyList<TableInfo> Tables => Exact.Tables;

    /// <summary>Gets the 1-based row index inferred as the header row, or 0 if none could be inferred.</summary>
    public int InferredHeaderRowIndex => RequireInferred().HeaderRowIndex;

    /// <summary>Gets a value indicating whether the sheet contains no data cells.</summary>
    public bool IsEmpty => RequireObserved().ValueUsedRange is null;

    private SheetAnalysisObserved RequireObserved()
    {
        if (!HasObserved)
        {
            throw new InvalidOperationException("Observed analysis data is not available for this result. Analyze with AnalysisLevel.Observed or AnalysisLevel.Full.");
        }

        return Observed;
    }

    private SheetAnalysisInferred RequireInferred()
    {
        if (!HasInferred)
        {
            throw new InvalidOperationException("Inferred analysis data is not available for this result. Analyze with AnalysisLevel.Full.");
        }

        return Inferred;
    }
}
