using XLSight.Models;
using XLSight.Models.Analysis;
using XLSight.Styles;

namespace XLSight.Worksheets;

internal struct RangeReadSink : IWorksheetSink
{
    private readonly ExcelRange _range;
    private readonly ExcelCellValue[] _buffer;
    private readonly string[] _sharedStrings;
    private readonly StyleTable _styles;
    private readonly bool _isDate1904;
    private readonly ExcelReadMode _mode;
    private bool _pastEnd;

    internal RangeReadSink(
        ExcelRange range,
        ExcelCellValue[] buffer,
        string[] sharedStrings,
        StyleTable styles,
        bool isDate1904,
        ExcelReadMode mode)
    {
        _range = range;
        _buffer = buffer;
        _sharedStrings = sharedStrings;
        _styles = styles;
        _isDate1904 = isDate1904;
        _mode = mode;
        _pastEnd = false;
    }

    public void OnDimension(in ExcelRange dimension) { }

    public void OnRowStart(int rowIndex)
    {
        if (rowIndex > _range.BottomRight.Row)
        {
            _pastEnd = true;
        }
    }

    public bool OnCell(in ParsedCell cell)
    {
        if (_pastEnd)
        {
            return false;
        }

        if (!_range.Contains(new ExcelAddress(cell.Column, cell.Row)))
        {
            return true;
        }

        var value = CellValueDecoder.Decode(in cell, _sharedStrings, _styles, _isDate1904, _mode);

        int rowOffset = cell.Row - _range.TopLeft.Row;
        int colOffset = cell.Column - _range.TopLeft.Column;
        int index = rowOffset * _range.Width + colOffset;

        if ((uint)index < (uint)_buffer.Length)
        {
            _buffer[index] = value;
        }

        return true;
    }

    public void OnMergeCell(in ExcelMergedRegion region) { }
    public void OnEnd() { }
}
