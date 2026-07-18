using XLSight.Layout.Internal;

namespace XLSight.Layout;

/// <summary>Provides optional worksheet layout analysis for <see cref="ExcelWorkbook"/>.</summary>
public static class ExcelWorkbookLayoutExtensions
{
    /// <summary>Analyzes a worksheet's axes, measure fields, and layout groups.</summary>
    /// <param name="workbook">The workbook to scan.</param>
    /// <param name="sheet">The worksheet name.</param>
    /// <returns>The inferred worksheet layout, or an empty result when the sheet has no cells.</returns>
    public static SheetLayoutInfo AnalyzeLayout(this ExcelWorkbook workbook, string sheet)
    {
        ArgumentNullException.ThrowIfNull(workbook);
        ArgumentNullException.ThrowIfNull(sheet);

        var sink = new LayoutScanSink();
        workbook.ScanWorksheet(sheet, ref sink);
        return SheetLayoutInference.Infer(sink.Cells);
    }

    /// <summary>Asynchronously analyzes a worksheet's axes, measure fields, and layout groups.</summary>
    /// <param name="workbook">The workbook to scan.</param>
    /// <param name="sheet">The worksheet name.</param>
    /// <param name="ct">A cancellation token.</param>
    /// <returns>A task that returns the inferred worksheet layout, or an empty result when the sheet has no cells.</returns>
    public static Task<SheetLayoutInfo> AnalyzeLayoutAsync(
        this ExcelWorkbook workbook,
        string sheet,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(workbook);
        ArgumentNullException.ThrowIfNull(sheet);
        return AnalyzeLayoutAsyncCore(workbook, sheet, ct);
    }

    /// <summary>Infers a worksheet's axes, measure fields, and layout groups.</summary>
    /// <param name="workbook">The workbook to scan.</param>
    /// <param name="sheet">The worksheet name.</param>
    /// <returns>The inferred worksheet layout, or an empty result when the sheet has no cells.</returns>
    public static SheetLayoutInfo InferLayout(this ExcelWorkbook workbook, string sheet) =>
        AnalyzeLayout(workbook, sheet);

    /// <summary>Asynchronously infers a worksheet's axes, measure fields, and layout groups.</summary>
    /// <param name="workbook">The workbook to scan.</param>
    /// <param name="sheet">The worksheet name.</param>
    /// <param name="ct">A cancellation token.</param>
    /// <returns>A task that returns the inferred worksheet layout, or an empty result when the sheet has no cells.</returns>
    public static Task<SheetLayoutInfo> InferLayoutAsync(
        this ExcelWorkbook workbook,
        string sheet,
        CancellationToken ct = default) =>
        AnalyzeLayoutAsync(workbook, sheet, ct);

    private static async Task<SheetLayoutInfo> AnalyzeLayoutAsyncCore(
        ExcelWorkbook workbook,
        string sheet,
        CancellationToken ct)
    {
        LayoutScanSink sink = await workbook
            .ScanWorksheetAsync(sheet, new LayoutScanSink(), ct)
            .ConfigureAwait(false);
        ct.ThrowIfCancellationRequested();
        return SheetLayoutInference.Infer(sink.Cells, ct);
    }
}
