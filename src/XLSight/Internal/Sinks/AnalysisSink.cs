using System.Runtime.InteropServices;
using XLSight.Models;
using XLSight.Models.Analysis;

namespace XLSight.Internal.Sinks;

[StructLayout(LayoutKind.Auto)]
internal struct AnalysisSink : IByteSheetSink
{
    private int _minRow;
    private int _maxRow;
    private int _minCol;
    private int _maxCol;
    private int _cellCount;
    private int _currentRow;

    private Dictionary<int, ColumnState> _columnStates;
    private List<MergedRegion> _mergedRegions;

    // column → first-row cell value (for header inference)
    private int _firstRowIndex;
    private Dictionary<int, ExcelCellValue>? _firstRowByColumn;

    public AnalysisSink()
    {
        _minRow = int.MaxValue;
        _maxRow = int.MinValue;
        _minCol = int.MaxValue;
        _maxCol = int.MinValue;
        _cellCount = 0;
        _currentRow = 0;

        _columnStates = [];
        _mergedRegions = [];

        _firstRowIndex = 0;
        _firstRowByColumn = null;
    }

    public void OnDimension(in ExcelRange dimension) { }

    public void OnRowStart(int rowIndex)
    {
        _currentRow = rowIndex;

        if (_firstRowIndex == 0)
        {
            _firstRowIndex = rowIndex;
            _firstRowByColumn = [];
        }
    }

    public bool OnCell(int column, CellDataKind kind, int styleIdx, ExcelCellValue value)
    {
        UpdateBounds(_currentRow, column);

        if (!value.IsEmpty)
        {
            _cellCount++;
            var state = GetOrAddColumnState(column);
            state.RecordValue(value);

            if (kind == CellDataKind.FormulaString)
            {
                state.HasFormulas = true;
            }
        }

        if (_currentRow == _firstRowIndex && _firstRowByColumn is not null)
        {
            _firstRowByColumn[column] = value;
        }

        return true;
    }

    public void OnMergeCell(in MergedRegion region)
    {
        _mergedRegions.Add(region);
    }

    public void OnEnd() { }

    internal SheetInfo Build(
        string sheetName,
        int sheetIndex,
        IReadOnlyList<TableInfo> tables)
    {
        bool isEmpty = _minRow == int.MaxValue;

        if (isEmpty)
        {
            return BuildEmpty(sheetName, sheetIndex, tables);
        }

        return BuildPopulated(sheetName, sheetIndex, tables);
    }

    private SheetInfo BuildEmpty(string sheetName, int sheetIndex, IReadOnlyList<TableInfo> tables)
    {
        return new SheetInfo
        {
            SheetName = sheetName,
            SheetIndex = sheetIndex,
            UsedRange = null,
            RowCount = 0,
            ColumnCount = 0,
            CellCount = 0,
            Columns = [],
            FormulaColumns = [],
            MergedRegions = _mergedRegions,
            Tables = tables,
            InferredHeaderRowIndex = 0,
            IsEmpty = true,
        };
    }

    private SheetInfo BuildPopulated(string sheetName, int sheetIndex, IReadOnlyList<TableInfo> tables)
    {
        int rowCount = _maxRow - _minRow + 1;
        int colCount = _maxCol - _minCol + 1;

        var usedRange = new ExcelRange(
            new ExcelAddress(_minCol, _minRow),
            new ExcelAddress(_maxCol, _maxRow));

        int headerRow = InferHeader(rowCount);
        var headersByColumn = BuildHeaders(headerRow);

        var columns = ColumnProfiler.BuildProfiles(_columnStates, headersByColumn);
        var formulaColumns = BuildFormulaColumnList();

        return new SheetInfo
        {
            SheetName = sheetName,
            SheetIndex = sheetIndex,
            UsedRange = usedRange,
            RowCount = rowCount,
            ColumnCount = colCount,
            CellCount = _cellCount,
            Columns = columns,
            FormulaColumns = formulaColumns,
            MergedRegions = _mergedRegions,
            Tables = tables,
            InferredHeaderRowIndex = headerRow,
            IsEmpty = false,
        };
    }

    private int InferHeader(int rowCount)
    {
        if (_firstRowByColumn is null || _firstRowByColumn.Count == 0)
        {
            return 0;
        }

        var cells = _firstRowByColumn.Values.ToArray();
        return HeaderInference.Infer(_firstRowIndex, cells, rowCount);
    }

    private Dictionary<int, string> BuildHeaders(int headerRowIndex)
    {
        if (headerRowIndex == 0 || _firstRowByColumn is null)
        {
            return [];
        }

        var result = new Dictionary<int, string>();
        foreach (var (col, value) in _firstRowByColumn)
        {
            if (value.CellType == CellType.Text)
            {
                result[col] = value.AsText();
            }
        }

        return result;
    }

    private List<string> BuildFormulaColumnList()
    {
        var list = new List<string>();
        foreach (var (col, state) in _columnStates.OrderBy(kv => kv.Key))
        {
            if (state.HasFormulas)
            {
                list.Add(new ExcelAddress(col, 1).ToString()[..^1]);
            }
        }

        return list;
    }

    private void UpdateBounds(int row, int col)
    {
        if (row < _minRow) { _minRow = row; }
        if (row > _maxRow) { _maxRow = row; }
        if (col < _minCol) { _minCol = col; }
        if (col > _maxCol) { _maxCol = col; }
    }

    private ColumnState GetOrAddColumnState(int col)
    {
        if (!_columnStates.TryGetValue(col, out var state))
        {
            state = new ColumnState();
            _columnStates[col] = state;
        }

        return state;
    }
}

