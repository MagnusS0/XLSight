using System.Runtime.InteropServices;

using XLSight.Models;
using XLSight.Models.Analysis;
using XLSight.Styles;

namespace XLSight.Worksheets;

[StructLayout(LayoutKind.Auto)]
internal struct StreamingSink : IWorksheetSink
{
    private readonly ExcelRange _range;
    private readonly string[] _sharedStrings;
    private readonly StyleTable _styles;
    private readonly bool _isDate1904;
    private readonly ExcelReadMode _mode;
    private readonly List<ExcelRow> _rows;
    private Dictionary<int, ExcelCellValue>? _currentRowCells;
    private int _currentRowIndex;
    private bool _pastEnd;

    internal StreamingSink(
        ExcelRange range,
        string[] sharedStrings,
        StyleTable styles,
        bool isDate1904,
        ExcelReadMode mode)
    {
        _range = range;
        _sharedStrings = sharedStrings;
        _styles = styles;
        _isDate1904 = isDate1904;
        _mode = mode;
        _rows = [];
        _currentRowCells = null;
        _currentRowIndex = 0;
        _pastEnd = false;
    }

    public void OnDimension(in ExcelRange dimension) { }

    public void OnRowStart(int rowIndex)
    {
        FlushCurrentRow();

        _currentRowIndex = rowIndex;
        _currentRowCells = [];

        if (!_range.IsUnbounded && rowIndex > _range.BottomRight.Row)
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
        (_currentRowCells ??= []).Add(cell.Column, value);

        return true;
    }

    public void OnMergeCell(in ExcelMergedRegion region) { }

    public void OnEnd()
    {
        FlushCurrentRow();
    }

    public List<ExcelRow> Rows => _rows;

    private void FlushCurrentRow()
    {
        if (_currentRowIndex == 0 || _currentRowCells is null || _currentRowCells.Count == 0)
        {
            _currentRowCells = null;
            return;
        }

        if (!_range.IsUnbounded && _currentRowIndex < _range.TopLeft.Row)
        {
            _currentRowCells = null;
            return;
        }

        var row = BuildRow(_currentRowIndex, _currentRowCells);
        _rows.Add(row);
        _currentRowCells = null;
    }

    private static ExcelRow BuildRow(int rowIndex, Dictionary<int, ExcelCellValue> cells)
    {
        int minCol = int.MaxValue;
        int maxCol = int.MinValue;

        foreach (var col in cells.Keys)
        {
            if (col < minCol)
            {
                minCol = col;
            }

            if (col > maxCol)
            {
                maxCol = col;
            }
        }

        int width = maxCol - minCol + 1;
        var buffer = new ExcelCellValue[width];

        foreach (var (col, value) in cells)
        {
            buffer[col - minCol] = value;
        }

        return new ExcelRow(rowIndex, buffer, minCol);
    }
}
