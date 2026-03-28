using XLSight.Models;
using XLSight.Models.Analysis;

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

    public void OnDimension(in ExcelRange dimension) { }

    public void OnRowStart(int rowIndex)
    {
        _currentRow = rowIndex;

        if (!_range.IsUnbounded && rowIndex > _range.BottomRight.Row)
        {
            _pastEnd = true;
        }
    }

    public bool OnCell(int column, CellDataKind kind, int styleIdx, ExcelCellValue value)
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

    public void OnMergeCell(in MergedRegion region) { }
    public void OnEnd() { }
}
