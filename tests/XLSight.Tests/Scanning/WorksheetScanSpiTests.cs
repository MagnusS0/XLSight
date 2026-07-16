using System.Runtime.InteropServices;
using XLSight.Internal.Scanning;
using XLSight.Internal.Sinks;
using Xunit;
using CellEvent = (int Row, int Column, XLSight.ExcelCellValue Value, bool IsFormula);

namespace XLSight.Tests.Scanning;

public sealed class WorksheetScanSpiTests
{
    [Fact]
    public void ScanWorksheet_PairedXlsxAndXlsb_EmitEquivalentCells()
    {
        List<CellEvent> expected = Scan("string_heavy.xlsx", "Strings");
        List<CellEvent> actual = Scan("string_heavy.xlsb", "Strings");

        Assert.NotEmpty(expected);
        Assert.Equal(expected.Count, actual.Count);
        for (int i = 0; i < expected.Count; i++)
        {
            AssertEquivalent(expected[i], actual[i]);
        }
    }

    [Fact]
    public void ScanWorksheet_PairedFormulaFixture_MarksEquivalentFormulaCells()
    {
        List<CellEvent> expectedCells = Scan("complex_workbook.xlsx", "Scenarios");
        List<CellEvent> actualCells = Scan("complex_workbook.xlsb", "Scenarios");

        AssertKnownFormulaMarkers(expectedCells);
        AssertKnownFormulaMarkers(actualCells);

        List<CellEvent> expected = expectedCells.Where(cell => cell.IsFormula).ToList();
        List<CellEvent> actual = actualCells.Where(cell => cell.IsFormula).ToList();

        Assert.NotEmpty(expected);
        Assert.Equal(expected.Count, actual.Count);
        for (int i = 0; i < expected.Count; i++)
        {
            AssertEquivalent(expected[i], actual[i]);
        }
    }

    [Fact]
    public void Adapter_ValueLessFormula_DoesNotMarkFollowingCellAsFormula()
    {
        List<CellEvent> cells = [];
        var adapter = new WorksheetScanAdapter<RecordingSink>(new RecordingSink(cells));
        adapter.OnRowStart(1);
        adapter.OnFormula(1, isArray: false);

        adapter.OnCell(1, CellDataKind.Number, styleIdx: 0, ExcelCellValue.Empty, rawIndex: -1);
        adapter.OnCell(2, CellDataKind.Number, styleIdx: 0, ExcelCellValue.FromNumber(1), rawIndex: -1);

        Assert.Collection(
            cells,
            cell => Assert.True(cell.IsFormula),
            cell => Assert.False(cell.IsFormula));
    }

    private static List<CellEvent> Scan(string fileName, string sheetName)
    {
        using var workbook = ExcelWorkbook.Open(GetTestDataPath(fileName));
        List<CellEvent> cells = [];
        var sink = new RecordingSink(cells);

        workbook.ScanWorksheet(sheetName, ref sink);

        return cells;
    }

    private static void AssertKnownFormulaMarkers(List<CellEvent> cells)
    {
        Assert.Contains(cells, cell => cell is { Row: 5, Column: 11, IsFormula: true });
        Assert.Contains(cells, cell => cell is { Row: 28, Column: 14, IsFormula: true });
        Assert.Contains(cells, cell => cell is { Row: 5, Column: 10, IsFormula: false });
    }

    private static void AssertEquivalent(CellEvent expected, CellEvent actual)
    {
        Assert.Multiple(
            () => Assert.Equal(expected.Row, actual.Row),
            () => Assert.Equal(expected.Column, actual.Column),
            () => Assert.Equal(expected.IsFormula, actual.IsFormula));

        Assert.Equal(expected.Value.CellType, actual.Value.CellType);
        if (expected.Value.CellType == CellType.Number)
        {
            Assert.Equal(expected.Value.AsNumber(), actual.Value.AsNumber(), precision: 10);
            return;
        }

        Assert.Equal(expected.Value, actual.Value);
    }

    private static string GetTestDataPath(string fileName) =>
        Path.Combine(AppContext.BaseDirectory, "TestData", fileName);

    [StructLayout(LayoutKind.Auto)]
    private readonly struct RecordingSink(List<CellEvent> cells) : IWorksheetScanSink
    {
        public void OnCell(int row, int column, in ExcelCellValue value, bool isFormula) =>
            cells.Add((row, column, value, isFormula));
    }
}
