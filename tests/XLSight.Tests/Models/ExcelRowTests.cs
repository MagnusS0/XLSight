using System.Collections;
using XLSight.Models;
using Xunit;

namespace XLSight.Tests.Models;

public sealed class ExcelRowTests
{
    // ── Factory helpers ──────────────────────────────────────────────────────

    private static ExcelRow MakeRow(int rowIndex, int columnOffset, params double[] values)
    {
        ExcelCellValue[] cells = values.Select(ExcelCellValue.FromNumber).ToArray();
        return new ExcelRow(rowIndex, cells, columnOffset);
    }

    private static ExcelRow EmptyRow(int rowIndex = 1, int columnOffset = 1)
        => new ExcelRow(rowIndex, Array.Empty<ExcelCellValue>(), columnOffset);

    // ── Basic properties ─────────────────────────────────────────────────────

    [Fact]
    public void RowIndex_ReturnsConstructorValue()
    {
        var row = MakeRow(7, 1, 1.0);
        Assert.Equal(7, row.RowIndex);
    }

    [Fact]
    public void CellCount_MatchesCellsLength()
    {
        var row = MakeRow(1, 1, 1.0, 2.0, 3.0);
        Assert.Equal(3, row.CellCount);
    }

    [Fact]
    public void StartColumn_ReturnsColumnOffset()
    {
        var row = MakeRow(1, 5, 10.0);
        Assert.Equal(5, row.StartColumn);
    }

    [Fact]
    public void Cells_SpanMatchesCellArray()
    {
        var row = MakeRow(1, 1, 10.0, 20.0);
        Assert.Equal(2, row.Cells.Length);
        Assert.Equal(ExcelCellValue.FromNumber(10.0), row.Cells[0]);
        Assert.Equal(ExcelCellValue.FromNumber(20.0), row.Cells[1]);
    }

    // ── GetCell ──────────────────────────────────────────────────────────────

    [Fact]
    public void GetCell_InRange_ReturnsCorrectValue()
    {
        var row = MakeRow(1, 3, 100.0, 200.0);
        Assert.Equal(ExcelCellValue.FromNumber(100.0), row.GetCell(3));
        Assert.Equal(ExcelCellValue.FromNumber(200.0), row.GetCell(4));
    }

    [Fact]
    public void GetCell_BeforeStart_ReturnsEmpty()
    {
        var row = MakeRow(1, 3, 100.0);
        Assert.Equal(ExcelCellValue.Empty, row.GetCell(2));
        Assert.Equal(ExcelCellValue.Empty, row.GetCell(1));
    }

    [Fact]
    public void GetCell_AfterEnd_ReturnsEmpty()
    {
        var row = MakeRow(1, 3, 100.0);
        Assert.Equal(ExcelCellValue.Empty, row.GetCell(4));
        Assert.Equal(ExcelCellValue.Empty, row.GetCell(100));
    }

    // ── GetCellRef ───────────────────────────────────────────────────────────

    [Fact]
    public void GetCellRef_InRange_ReturnsRefToValue()
    {
        var row = MakeRow(1, 1, 42.0);
        ref readonly ExcelCellValue cellRef = ref row.GetCellRef(1);
        Assert.Equal(ExcelCellValue.FromNumber(42.0), cellRef);
    }

    [Fact]
    public void GetCellRef_BeforeStart_ReturnsRefToEmpty()
    {
        var row = MakeRow(1, 3, 42.0);
        ref readonly ExcelCellValue cellRef = ref row.GetCellRef(1);
        Assert.Equal(ExcelCellValue.Empty, cellRef);
    }

    [Fact]
    public void GetCellRef_AfterEnd_ReturnsRefToEmpty()
    {
        var row = MakeRow(1, 1, 42.0);
        ref readonly ExcelCellValue cellRef = ref row.GetCellRef(5);
        Assert.Equal(ExcelCellValue.Empty, cellRef);
    }

    // ── Struct enumerator (GetEnumerator / foreach) ──────────────────────────

    [Fact]
    public void StructEnumerator_ForeachOverRow_VisitsAllCells()
    {
        var row = MakeRow(1, 1, 1.0, 2.0, 3.0);
        var collected = new List<ExcelCellValue>();
        foreach (ExcelCellValue cell in row)
        {
            collected.Add(cell);
        }

        Assert.Equal(3, collected.Count);
        Assert.Equal(ExcelCellValue.FromNumber(1.0), collected[0]);
        Assert.Equal(ExcelCellValue.FromNumber(2.0), collected[1]);
        Assert.Equal(ExcelCellValue.FromNumber(3.0), collected[2]);
    }

    [Fact]
    public void StructEnumerator_EmptyRow_NoIterations()
    {
        var row = EmptyRow();
        var collected = new List<ExcelCellValue>();
        foreach (ExcelCellValue cell in row)
        {
            collected.Add(cell);
        }

        Assert.Empty(collected);
    }

    [Fact]
    public void StructEnumerator_MoveNext_ReturnsFalseWhenExhausted()
    {
        var row = MakeRow(1, 1, 1.0);
        var enumerator = row.GetEnumerator();
        Assert.True(enumerator.MoveNext());
        Assert.False(enumerator.MoveNext());
    }

    [Fact]
    public void StructEnumerator_Current_ReturnsCurrentCell()
    {
        var row = MakeRow(1, 1, 42.0);
        var enumerator = row.GetEnumerator();
        enumerator.MoveNext();
        Assert.Equal(ExcelCellValue.FromNumber(42.0), enumerator.Current);
    }

    // ── IEnumerable<ExcelCellValue> explicit interface ───────────────────────

    [Fact]
    public void ExplicitGenericEnumerator_VisitsAllCells()
    {
        var row = MakeRow(1, 1, 5.0, 6.0);
        IEnumerable<ExcelCellValue> enumerable = row;
        List<ExcelCellValue> collected = enumerable.ToList();

        Assert.Equal(2, collected.Count);
        Assert.Equal(ExcelCellValue.FromNumber(5.0), collected[0]);
        Assert.Equal(ExcelCellValue.FromNumber(6.0), collected[1]);
    }

    [Fact]
    public void ExplicitGenericEnumerator_EmptyRow_ReturnsEmpty()
    {
        IEnumerable<ExcelCellValue> enumerable = EmptyRow();
        Assert.Empty(enumerable);
    }

    // ── IEnumerable non-generic explicit interface ───────────────────────────

    [Fact]
    public void ExplicitNonGenericEnumerator_VisitsAllCells()
    {
        var row = MakeRow(1, 1, 7.0, 8.0);
        IEnumerable enumerable = row;
        var collected = new List<object?>();
        foreach (var cell in enumerable)
        {
            collected.Add(cell);
        }

        Assert.Equal(2, collected.Count);
    }

    // ── CloneRow ─────────────────────────────────────────────────────────────

    [Fact]
    public void CloneRow_CreatesIndependentCopy()
    {
        var cells = new[] { ExcelCellValue.FromNumber(1.0), ExcelCellValue.FromNumber(2.0) };
        var row = new ExcelRow(3, cells, 2);
        ExcelRow clone = row.CloneRow();

        Assert.Equal(row.RowIndex, clone.RowIndex);
        Assert.Equal(row.StartColumn, clone.StartColumn);
        Assert.Equal(row.CellCount, clone.CellCount);
        Assert.Equal(row.GetCell(2), clone.GetCell(2));
        Assert.Equal(row.GetCell(3), clone.GetCell(3));
    }

    [Fact]
    public void CloneRow_EmptyRow_ClonesCorrectly()
    {
        var row = EmptyRow(rowIndex: 5, columnOffset: 1);
        ExcelRow clone = row.CloneRow();
        Assert.Equal(0, clone.CellCount);
        Assert.Equal(5, clone.RowIndex);
    }

    // ── Empty row edge cases ─────────────────────────────────────────────────

    [Fact]
    public void EmptyRow_DefaultConstructor_AllPropertiesAreDefault()
    {
        var row = default(ExcelRow);
        Assert.Equal(0, row.RowIndex);
        Assert.Equal(0, row.CellCount);
        Assert.Equal(0, row.StartColumn);
        Assert.True(row.Cells.IsEmpty);
    }

    [Fact]
    public void GetCell_OnEmptyRow_ReturnsEmpty()
    {
        var row = EmptyRow();
        Assert.Equal(ExcelCellValue.Empty, row.GetCell(1));
    }

    // ── Non-default column offsets ────────────────────────────────────────────

    [Fact]
    public void GetCell_WithNonDefaultOffset_ComputesCorrectIndex()
    {
        // Row starts at column 10: cells at col 10, 11, 12
        var row = MakeRow(1, 10, 100.0, 200.0, 300.0);
        Assert.Equal(ExcelCellValue.Empty, row.GetCell(9));
        Assert.Equal(ExcelCellValue.FromNumber(100.0), row.GetCell(10));
        Assert.Equal(ExcelCellValue.FromNumber(200.0), row.GetCell(11));
        Assert.Equal(ExcelCellValue.FromNumber(300.0), row.GetCell(12));
        Assert.Equal(ExcelCellValue.Empty, row.GetCell(13));
    }
}
