using XLSight.Analysis;
using Xunit;

namespace XLSight.Tests.Readers.Xlsb;

public sealed class XlsbRealWorkbookTests
{
    [Fact]
    public void StreamSheet_LargeWorkbook_First10RowsHaveExpectedRowIndexes()
    {
        using var workbook = ExcelWorkbook.Open(GetTestDataPath("large.xlsb"));

        ExcelRow[] rows = workbook.StreamSheet("Numbers").Take(10).ToArray();

        Assert.Equal(10, rows.Length);
        Assert.Equal(Enumerable.Range(1, 10), rows.Select(row => row.RowIndex));
        Assert.All(rows, row => Assert.True(XLSightTestHelpers.RowHasValue(row)));
    }

    [Fact]
    public void SheetReader_LargeWorkbook_FirstRowsArePopulated()
    {
        using var workbook = ExcelWorkbook.Open(GetTestDataPath("large.xlsb"));
        using var reader = workbook.GetSheetReader("Numbers");

        Assert.True(reader.Read());
        Assert.Equal(1, reader.Current.RowIndex);
        Assert.True(XLSightTestHelpers.RowHasValue(reader.Current));

        Assert.True(reader.Read());
        Assert.Equal(2, reader.Current.RowIndex);
        Assert.True(XLSightTestHelpers.RowHasValue(reader.Current));
    }

    [Fact]
    public void ReadRange_ComplexWorkbook_MidRangeReturnsPopulatedCells()
    {
        using var workbook = ExcelWorkbook.Open(GetTestDataPath("complex_workbook.xlsb"));

        RangeResult result = workbook.ReadRange("Scenarios", "B10:N20");

        Assert.Equal(13, result.Width);
        Assert.Equal(11, result.Height);
        Assert.Contains(result.Cells.ToArray(), cell => cell.HasValue);
    }

    [Theory]
    [InlineData("large", "Numbers", "A1:E10")]
    [InlineData("string_heavy", "Strings", "A2:E20")]
    [InlineData("complex_workbook", "Scenarios", "B10:N20")]
    public void ReadRange_XlsbMatchesPairedXlsxValues(string workbookName, string sheetName, string rangeAddress)
    {
        using var xlsx = ExcelWorkbook.Open(GetTestDataPath($"{workbookName}.xlsx"));
        using var xlsb = ExcelWorkbook.Open(GetTestDataPath($"{workbookName}.xlsb"));

        RangeResult expected = xlsx.ReadRange(sheetName, rangeAddress);
        RangeResult actual = xlsb.ReadRange(sheetName, rangeAddress);

        Assert.Equal(expected.Width, actual.Width);
        Assert.Equal(expected.Height, actual.Height);
        Assert.Equal(expected.Cells.Length, actual.Cells.Length);
        for (int i = 0; i < expected.Cells.Length; i++)
        {
            AssertCellEqual(expected.Cells.Span[i], actual.Cells.Span[i], i);
        }
    }

    [Fact]
    public void ReadRange_Formulas_XlsbMatchesPairedXlsxFormulas()
    {
        using var xlsx = ExcelWorkbook.Open(GetTestDataPath("complex_workbook.xlsx"));
        using var xlsb = ExcelWorkbook.Open(GetTestDataPath("complex_workbook.xlsb"));

        RangeResult expected = xlsx.ReadRange("Scenarios", "J10:J20", ReadMode.Formulas);
        RangeResult actual = xlsb.ReadRange("Scenarios", "J10:J20", ReadMode.Formulas);

        Assert.Equal(expected.Cells.Length, actual.Cells.Length);
        for (int i = 0; i < expected.Cells.Length; i++)
        {
            AssertCellEqual(expected.Cells.Span[i], actual.Cells.Span[i], i);
        }
    }

    private static string GetTestDataPath(string fileName) =>
        Path.Combine(AppContext.BaseDirectory, "TestData", fileName);

    private static void AssertCellEqual(ExcelCellValue expected, ExcelCellValue actual, int index)
    {
        Assert.Equal(expected.CellType, actual.CellType);
        switch (expected.CellType)
        {
            case CellType.Empty:
                Assert.True(actual.IsEmpty);
                break;
            case CellType.Number:
                Assert.Equal(expected.AsNumber(), actual.AsNumber(), precision: 10);
                break;
            case CellType.Text:
                Assert.Equal(expected.AsText(), actual.AsText());
                break;
            case CellType.Boolean:
                Assert.Equal(expected.AsBoolean(), actual.AsBoolean());
                break;
            case CellType.Error:
                Assert.Equal(expected.AsError(), actual.AsError());
                break;
            case CellType.Date:
                Assert.Equal(expected.AsDate(), actual.AsDate());
                break;
            case CellType.Formula:
                Assert.Equal(expected.AsFormula(), actual.AsFormula());
                break;
            default:
                throw new Xunit.Sdk.XunitException($"Unexpected cell type at index {index}: {expected.CellType}.");
        }
    }

    [Theory]
    [InlineData("large.xlsb", "Numbers", 100, 5)]
    [InlineData("string_heavy.xlsb", "Strings", 100, 5)]
    [InlineData("complex_workbook.xlsb", "Scenarios", 10, 5)]
    public void AnalyzeSheet_XlsbFixtures_ReturnsObservedShape(
        string fileName,
        string sheetName,
        int minimumRows,
        int minimumColumns)
    {
        using var workbook = ExcelWorkbook.Open(GetTestDataPath(fileName));

        SheetInfo info = workbook.AnalyzeSheet(sheetName);

        Assert.Equal(AnalysisLevel.Full, info.Level);
        Assert.Equal(sheetName, info.SheetName);
        Assert.True(info.HasObserved);
        var observed = Assert.IsType<SheetAnalysisObserved>(info.Observed);
        Assert.NotNull(observed.ValueUsedRange);
        Assert.True(observed.RowCount >= minimumRows);
        Assert.True(observed.ColumnCount >= minimumColumns);
        Assert.True(observed.CellCount >= observed.RowCount);
        Assert.NotEmpty(observed.Columns);
        Assert.Equal(observed.ColumnCount, observed.Columns.Count);
        Assert.All(observed.Columns, column => Assert.True(column.NonEmptyCount > 0));
    }

    [Theory]
    [InlineData("large", "Numbers")]
    [InlineData("string_heavy", "Strings")]
    [InlineData("complex_workbook", "Scenarios")]
    public void AnalyzeSheet_XlsbMatchesPairedXlsxObservedCounts(string workbookName, string sheetName)
    {
        using var xlsx = ExcelWorkbook.Open(GetTestDataPath($"{workbookName}.xlsx"));
        using var xlsb = ExcelWorkbook.Open(GetTestDataPath($"{workbookName}.xlsb"));

        SheetInfo expected = xlsx.AnalyzeSheet(sheetName, AnalysisLevel.Observed);
        SheetInfo actual = xlsb.AnalyzeSheet(sheetName, AnalysisLevel.Observed);

        var expectedObserved = Assert.IsType<SheetAnalysisObserved>(expected.Observed);
        var actualObserved = Assert.IsType<SheetAnalysisObserved>(actual.Observed);
        Assert.Equal(expectedObserved.ValueUsedRange, actualObserved.ValueUsedRange);
        Assert.Equal(expectedObserved.RowCount, actualObserved.RowCount);
        Assert.Equal(expectedObserved.ColumnCount, actualObserved.ColumnCount);
        Assert.Equal(expectedObserved.CellCount, actualObserved.CellCount);
        Assert.Equal(expectedObserved.FormulaCount, actualObserved.FormulaCount);
        Assert.Equal(expectedObserved.Columns.Count, actualObserved.Columns.Count);

        for (int i = 0; i < expectedObserved.Columns.Count; i++)
        {
            ColumnProfile expectedColumn = expectedObserved.Columns[i];
            ColumnProfile actualColumn = actualObserved.Columns[i];
            Assert.Equal(expectedColumn.ColumnIndex, actualColumn.ColumnIndex);
            Assert.Equal(expectedColumn.NonEmptyCount, actualColumn.NonEmptyCount);
            Assert.Equal(expectedColumn.DominantType, actualColumn.DominantType);
        }
    }

    [Fact]
    public void Analyze_XlsbWorkbook_ReturnsWorkbookAndSheetInfo()
    {
        using var workbook = ExcelWorkbook.Open(GetTestDataPath("complex_workbook.xlsb"));

        WorkbookInfo info = workbook.Analyze(AnalysisLevel.Observed, maxDegreeOfParallelism: 1);

        Assert.Equal(WorkbookFormat.Xlsb, workbook.Format);
        Assert.Equal(AnalysisLevel.Observed, info.Level);
        Assert.NotEmpty(info.Sheets);
        Assert.Equal(workbook.SheetNames.Count, info.Sheets.Count);
        Assert.All(info.Sheets, sheet => Assert.True(sheet.HasObserved));
        Assert.Contains(
            info.Sheets,
            sheet => string.Equals(sheet.SheetName, "Scenarios", StringComparison.Ordinal)
                && sheet.RowCount > 0
                && sheet.ColumnCount > 0);
    }

    [Fact]
    public void Analyze_ComplexWorkbook_SurfacesChartAndDrawingMetadata()
    {
        using var workbook = ExcelWorkbook.Open(GetTestDataPath("complex_workbook.xlsb"));

        WorkbookInfo info = workbook.Analyze(maxDegreeOfParallelism: 1);

        Assert.Equal(3, info.Charts.Count);
        Assert.All(info.Charts, chart => Assert.Equal("Charts", chart.Sheet));
        Assert.Contains(info.Charts, chart => chart.PartPath.EndsWith("chart1.xml", StringComparison.Ordinal));
        Assert.Contains(info.Charts, chart => chart.SourceReferences.Count > 0);
        Assert.Contains(
            info.Charts,
            chart => chart.SourceReferences.Contains("Cumulative Contributions", StringComparer.Ordinal));
        Assert.DoesNotContain(
            info.Charts.SelectMany(chart => chart.SourceReferences),
            source => source.Contains("[0]!", StringComparison.Ordinal));

        SheetInfo charts = Assert.Single(
            info.Sheets,
            sheet => string.Equals(sheet.SheetName, "Charts", StringComparison.Ordinal));
        Assert.Equal(1, charts.Exact.DrawingCount);
        Assert.Equal(3, charts.Exact.Charts.Count);
    }

    [Fact]
    public void Analyze_ComplexWorkbook_ExactLevel_SurfacesChartAndDrawingMetadata()
    {
        using var workbook = ExcelWorkbook.Open(GetTestDataPath("complex_workbook.xlsb"));

        WorkbookInfo info = workbook.Analyze(AnalysisLevel.Exact, maxDegreeOfParallelism: 1);

        Assert.Equal(AnalysisLevel.Exact, info.Level);
        Assert.Equal(3, info.Charts.Count);
        SheetInfo charts = Assert.Single(
            info.Sheets,
            sheet => string.Equals(sheet.SheetName, "Charts", StringComparison.Ordinal));
        Assert.Equal(1, charts.Exact.DrawingCount);
        Assert.Equal(3, charts.Exact.Charts.Count);
        Assert.Null(charts.Observed);
    }

    [Fact]
    public void Analyze_ComplexWorkbook_SurfacesPivotSourceReference()
    {
        using var workbook = ExcelWorkbook.Open(GetTestDataPath("complex_workbook.xlsb"));

        WorkbookInfo info = workbook.Analyze(AnalysisLevel.Exact, maxDegreeOfParallelism: 1);

        PivotTableInfo pivot = Assert.Single(info.PivotTables);
        Assert.Equal("pivotTable1", pivot.Name);
        Assert.Equal("Pivot-Analysis", pivot.Sheet);
        Assert.Equal(new ExcelRange(new ExcelAddress(2, 2), new ExcelAddress(9, 12)), pivot.Range);
        Assert.Equal("Scenarios!B4:N28", pivot.SourceReference);
    }

    [Fact]
    public async Task AnalyzeAsync_XlsbWorkbook_ReturnsWorkbookAndSheetInfo()
    {
        using var workbook = await ExcelWorkbook.OpenAsync(
            GetTestDataPath("large.xlsb"),
            TestContext.Current.CancellationToken);

        WorkbookInfo info = await workbook.AnalyzeAsync(
            AnalysisLevel.Observed,
            maxDegreeOfParallelism: 1,
            ct: TestContext.Current.CancellationToken);

        Assert.Equal(WorkbookFormat.Xlsb, workbook.Format);
        Assert.Equal(AnalysisLevel.Observed, info.Level);
        SheetInfo sheet = Assert.Single(info.Sheets);
        Assert.Equal("Numbers", sheet.SheetName);
        Assert.True(sheet.HasObserved);
        Assert.True(sheet.RowCount >= 100);
        Assert.True(sheet.ColumnCount >= 5);
    }
}
