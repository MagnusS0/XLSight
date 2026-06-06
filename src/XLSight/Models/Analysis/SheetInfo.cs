namespace XLSight.Analysis;

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

    /// <summary>Gets worksheet facts observed during the streaming value scan, when available.</summary>
    public SheetAnalysisObserved? Observed { get; init; }

    /// <summary>Gets inferred worksheet structure derived from exact and observed facts, when available.</summary>
    public SheetAnalysisInferred? Inferred { get; init; }

    /// <summary>Gets a value indicating whether observed scan data is available for this sheet.</summary>
    public bool HasObserved => Observed is not null;

    /// <summary>Gets a value indicating whether inferred structure is available for this sheet.</summary>
    public bool HasInferred => Inferred is not null;

    /// <summary>Gets the bounding range of all non-empty cells, or <see langword="null"/> when observed data is unavailable or the sheet is empty.</summary>
    public ExcelRange? UsedRange => Observed?.ValueUsedRange;

    /// <summary>Gets the number of non-empty rows in the used range, or <see langword="null"/> when observed data is unavailable.</summary>
    public int? RowCount => Observed?.RowCount;

    /// <summary>Gets the number of non-empty columns in the used range, or <see langword="null"/> when observed data is unavailable.</summary>
    public int? ColumnCount => Observed?.ColumnCount;

    /// <summary>Gets the total number of non-empty cells in the sheet, or <see langword="null"/> when observed data is unavailable.</summary>
    public int? CellCount => Observed?.CellCount;

    /// <summary>Gets the column-level profiles for each column that contains data, or <see langword="null"/> when observed data is unavailable.</summary>
    public IReadOnlyList<ColumnProfile>? Columns => Observed?.Columns;

    /// <summary>Gets the Excel-style column letters (e.g. "A", "BC") of columns that contain formulas, or <see langword="null"/> when observed data is unavailable.</summary>
    public IReadOnlyList<string>? FormulaColumns
    {
        get
        {
            if (Observed is null)
            {
                return null;
            }

            return _formulaColumns ??= [.. Observed.FormulaColumns.Select(c => c.ColumnLabel)];
        }
    }

    /// <summary>Gets all merged cell regions in this sheet.</summary>
    public IReadOnlyList<MergedRegion> MergedRegions => Exact.MergedRegions;

    /// <summary>Gets the structured tables defined in this sheet.</summary>
    public IReadOnlyList<TableInfo> Tables => Exact.Tables;

    /// <summary>Gets all data-validation rules defined in this sheet.</summary>
    public IReadOnlyList<DataValidationInfo> DataValidations => Exact.DataValidations;

    /// <summary>Gets cross-sheet and cross-workbook formula dependencies, or null when observed data is unavailable.</summary>
    public IReadOnlyList<FormulaDependencyInfo>? FormulaDependencies => Observed?.FormulaDependencies;

    /// <summary>Gets the 1-based row index inferred as the header row, or <see langword="null"/> when inferred data is unavailable.</summary>
    public int? InferredHeaderRowIndex => Inferred?.HeaderRowIndex;

    /// <summary>Gets a value indicating whether the sheet contains no data cells, or <see langword="null"/> when observed data is unavailable.</summary>
    public bool? IsEmpty => Observed is null ? null : Observed.ValueUsedRange is null;

    /// <summary>Gets the observed analysis data when available.</summary>
    public bool TryGetObserved([System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out SheetAnalysisObserved? observed)
    {
        observed = Observed;
        return observed is not null;
    }

    /// <summary>Gets the inferred analysis data when available.</summary>
    public bool TryGetInferred([System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out SheetAnalysisInferred? inferred)
    {
        inferred = Inferred;
        return inferred is not null;
    }
}
