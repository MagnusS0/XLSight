using XLSight.Analysis;

namespace XLSight.Internal.Sinks;

internal struct RangeSink : IByteSheetSink
{
    private readonly ExcelRange _range;
    private readonly ExcelCellValue[] _buffer;
    private int _currentRow;
    private bool _pastEnd;

    internal RangeSink(ExcelRange range, ExcelCellValue[] buffer)
    {
        _range = range;
        _buffer = buffer;
        _currentRow = 0;
        _pastEnd = false;
    }

    public bool NeedsDecodedValue => true;
    public bool TracksFormulas => false;
    public bool TracksFormulaReferences => false;

    public void OnDimension(in ExcelRange dimension) { }

    public void OnRowStart(int rowIndex)
    {
        _currentRow = rowIndex;

        if (!_range.IsUnbounded && rowIndex > _range.BottomRight.Row)
        {
            _pastEnd = true;
        }
    }

    public bool OnCell(int column, CellDataKind kind, int styleIdx, ExcelCellValue value, int rawIndex)
    {
        if (_pastEnd)
        {
            return false;
        }

        int rowOffset = _currentRow - _range.TopLeft.Row;
        int colOffset = column - _range.TopLeft.Column;
        int index = rowOffset * _range.Width + colOffset;

        if ((uint)index < (uint)_buffer.Length)
        {
            _buffer[index] = value;
        }

        return true;
    }

    public void OnFormula(int column, bool isArray) { }
    public void OnFormulaReference(in FormulaReference reference) { }
    public void OnSharedFormulaDefinition(int sharedIndex) { }
    public void OnSharedFormulaReference(int sharedIndex) { }
    public void OnMergeCell(in MergedRegion region) { }
    public void OnConditionalFormatting() { }
    public void OnDataValidation(DataValidationInfo? validation) { }
    public void OnHyperlink() { }
    public void OnEnd() { }
}
