using System.Buffers;
using System.Runtime.InteropServices;
using XLSight.Models;

namespace XLSight.Internal.Sinks;

internal partial struct AnalysisSink
{
    private int InferHeader()
    {
        if (_firstRowByColumn is null || _firstRowByColumn.Count == 0 || _minValueRow == int.MaxValue)
        {
            return 0;
        }

        // List was populated in column scan order — no sort needed.
        int count = _firstRowByColumn.Count;
        ExcelCellValue[] rented = ArrayPool<ExcelCellValue>.Shared.Rent(count);
        for (int i = 0; i < count; i++) { rented[i] = _firstRowByColumn[i].Value; }

        int rowCount = _maxValueRow - _minValueRow + 1;
        int result = HeaderInference.Infer(_firstRowIndex, rented.AsSpan(0, count), rowCount);
        ArrayPool<ExcelCellValue>.Shared.Return(rented, clearArray: false);
        return result;
    }

    private Dictionary<int, string> BuildHeaders(int headerRowIndex)
    {
        if (headerRowIndex == 0 || _firstRowByColumn is null) { return []; }

        var result = new Dictionary<int, string>(_firstRowByColumn.Count);
        foreach (var (column, value) in _firstRowByColumn)
        {
            if (value.CellType == CellType.Text)
            {
                result[column] = value.AsText();
            }
        }

        return result;
    }

    private void UpdateValueBounds(int row, int col)
    {
        if (row < _minValueRow) { _minValueRow = row; }
        if (row > _maxValueRow) { _maxValueRow = row; }
        if (col < _minValueCol) { _minValueCol = col; }
        if (col > _maxValueCol) { _maxValueCol = col; }
    }

    private void UpdateStyledBounds(int row, int col)
    {
        if (row < _minStyledRow) { _minStyledRow = row; }
        if (row > _maxStyledRow) { _maxStyledRow = row; }
        if (col < _minStyledCol) { _minStyledCol = col; }
        if (col > _maxStyledCol) { _maxStyledCol = col; }
    }

    private ColumnState GetOrAddColumnState(int column)
    {
        ref var state = ref CollectionsMarshal.GetValueRefOrAddDefault(_columnStates, column, out bool existed);
        if (!existed) { state = new ColumnState(); }
        return state!;
    }

    private static ExcelRange? BuildRange(int minCol, int minRow, int maxCol, int maxRow)
        => minRow == int.MaxValue
            ? null
            : new ExcelRange(new ExcelAddress(minCol, minRow), new ExcelAddress(maxCol, maxRow));

    private static string ColumnLabel(int column) => new ExcelAddress(column, 1).ToString()[..^1];
}
