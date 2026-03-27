using XLSight.Models;
using XLSight.Models.Analysis;

namespace XLSight.Worksheets;

/// <summary>
/// Push-based sink for the ByteEngine sheet scanner.
/// Implement as a <see langword="struct"/> and use with
/// <c>XlsxSheetScanner.ScanSheet&lt;TSink&gt;</c> to get zero virtual-dispatch throughput.
/// </summary>
internal interface IByteSheetSink
{
    /// <summary>Called when a <c>&lt;dimension&gt;</c> element is found with the sheet's declared used range.</summary>
    public void OnDimension(in ExcelRange dimension);

    /// <summary>Called at the start of each <c>&lt;row&gt;</c> element.</summary>
    public void OnRowStart(int rowIndex);

    /// <summary>
    /// Called for each cell with its decoded value.
    /// Return <see langword="false"/> to abort scanning early (e.g. range is past the end).
    /// </summary>
    /// <param name="column">1-based column index.</param>
    /// <param name="kind">Raw cell data kind (useful for formula-column detection).</param>
    /// <param name="styleIdx">Style index from the <c>s=</c> attribute.</param>
    /// <param name="value">Already-decoded cell value.</param>
    public bool OnCell(int column, CellDataKind kind, int styleIdx, ExcelCellValue value);

    /// <summary>Called for each <c>&lt;mergeCell&gt;</c> element.</summary>
    public void OnMergeCell(in ExcelMergedRegion region);

    /// <summary>Called when scanning completes (normal end or early termination).</summary>
    public void OnEnd();
}
