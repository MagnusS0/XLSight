using System.Runtime.InteropServices;
using XLSight.Analysis;
using XLSight.Internal.Sinks;

namespace XLSight.Internal.Scanning;

[StructLayout(LayoutKind.Auto)]
internal struct WorksheetScanAdapter<TSink>(TSink sink) : IByteSheetSink
    where TSink : struct, IWorksheetScanSink
{
    private TSink _sink = sink;
    private int _currentRow;
    private bool _nextCellIsFormula;

    internal readonly TSink Sink => _sink;

    public readonly bool NeedsDecodedValue => true;
    public readonly bool TracksFormulas => true;
    public readonly bool TracksFormulaReferences => false;

    public readonly void OnDimension(in ExcelRange dimension) { }

    public void OnRowStart(int rowIndex)
    {
        _currentRow = rowIndex;
        _nextCellIsFormula = false;
    }

    public void OnFormula(int column, bool isArray) => _nextCellIsFormula = true;

    public bool OnCell(int column, CellDataKind kind, int styleIdx, ExcelCellValue value, int rawIndex)
    {
        _sink.OnCell(_currentRow, column, in value, _nextCellIsFormula);
        _nextCellIsFormula = false;
        return true;
    }

    public readonly void OnFormulaReference(in FormulaReference reference) { }
    public readonly void OnSharedFormulaDefinition(int sharedIndex) { }
    public readonly void OnSharedFormulaReference(int sharedIndex) { }
    public readonly void OnMergeCell(in MergedRegion region) { }
    public readonly void OnConditionalFormatting() { }
    public readonly void OnDataValidation(DataValidationInfo? validation) { }
    public readonly void OnHyperlink() { }
    public readonly void OnEnd() { }
}
