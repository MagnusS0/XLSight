using XLSight.Models;
using XLSight.Models.Analysis;

namespace XLSight.Internal.Sinks;

/// <summary>
/// Push-based sink for the ByteEngine sheet scanner.
/// Implement as a <see langword="struct"/> and use with
/// <c>XlsxSheetScanner.ScanSheet&lt;TSink&gt;</c> to get zero virtual-dispatch throughput.
/// </summary>
internal interface IByteSheetSink
{
    /// <summary>
    /// When <see langword="false"/>, the scanner skips <see cref="CellDataKind.SharedString"/>
    /// string materialisation and passes <see cref="ExcelCellValue.Empty"/> for those cells,
    /// relying on the <c>rawIndex</c> parameter in <see cref="OnCell"/> instead.
    /// Implement as a compile-time constant so the JIT can dead-code-eliminate the decode path.
    /// </summary>
    public bool NeedsDecodedValue { get; }

    /// <summary>
    /// When <see langword="true"/>, the scanner peeks inside each cell for a <c>&lt;f&gt;</c> tag
    /// and calls <see cref="OnFormula"/> when one is found.
    /// Implement as a compile-time constant so the JIT can dead-code-eliminate the peek.
    /// </summary>
    public bool TracksFormulas { get; }

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
    /// <param name="rawIndex">
    /// For <see cref="CellDataKind.SharedString"/> cells: the raw SST integer index,
    /// allowing sinks to avoid string materialisation. <c>-1</c> for all other kinds.
    /// </param>
    public bool OnCell(int column, CellDataKind kind, int styleIdx, ExcelCellValue value, int rawIndex);

    /// <summary>
    /// Called when a formula (<c>&lt;f&gt;</c>) is detected inside a cell.
    /// Only called when <see cref="TracksFormulas"/> is <see langword="true"/>.
    /// </summary>
    public void OnFormula(int column, bool isArray);

    /// <summary>Called for each <c>&lt;mergeCell&gt;</c> element.</summary>
    public void OnMergeCell(in MergedRegion region);

    /// <summary>Called for each <c>&lt;conditionalFormatting&gt;</c> element found after <c>&lt;/sheetData&gt;</c>.</summary>
    public void OnConditionalFormatting();

    /// <summary>Called for each <c>&lt;dataValidation&gt;</c> element found after <c>&lt;/sheetData&gt;</c>.</summary>
    public void OnDataValidation();

    /// <summary>Called for each <c>&lt;hyperlink&gt;</c> element found after <c>&lt;/sheetData&gt;</c>.</summary>
    public void OnHyperlink();

    /// <summary>Called when scanning completes (normal end or early termination).</summary>
    public void OnEnd();
}
