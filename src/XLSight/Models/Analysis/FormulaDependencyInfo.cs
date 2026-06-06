namespace XLSight.Analysis;

/// <summary>Summarizes formulas on one sheet that reference another sheet or workbook.</summary>
public sealed class FormulaDependencyInfo
{
    /// <summary>Gets the referenced workbook name or relationship target, or null for this workbook.</summary>
    public string? TargetWorkbook { get; init; }

    /// <summary>Gets the referenced sheet name or 3D sheet range.</summary>
    public required string TargetSheet { get; init; }

    /// <summary>Gets the number of distinct formula cells that reference this target.</summary>
    public required int FormulaCount { get; init; }
}
