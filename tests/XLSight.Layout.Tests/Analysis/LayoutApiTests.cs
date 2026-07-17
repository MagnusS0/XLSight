using XLSight.Layout;
using Xunit;

namespace XLSight.Layout.Tests.Analysis;

public sealed class LayoutApiTests
{
    [Fact]
    public void AnalyzeLayout_EmptySheet_ReturnsEmptyResult()
    {
        using var stream = LayoutTestWorkbook.Build([]);
        using var workbook = ExcelWorkbook.Open(stream);

        SheetLayoutInfo layout = workbook.AnalyzeLayout("Data");

        Assert.Empty(layout.Axes);
        Assert.Empty(layout.MeasureFields);
        Assert.Empty(layout.Groups);
    }

    [Fact]
    public async Task AnalyzeLayoutAsync_ComplexWorksheet_MatchesSynchronousResult()
    {
        using var workbook = ExcelWorkbook.Open(TestDataFile("complex_workbook.xlsx"));
        SheetLayoutInfo expected = workbook.AnalyzeLayout("Calculator");

        SheetLayoutInfo actual = await workbook.AnalyzeLayoutAsync(
            "Calculator",
            TestContext.Current.CancellationToken);

        AssertEquivalent(expected, actual);
    }

    [Fact]
    public async Task AnalyzeLayoutAsync_CanceledToken_IsObserved()
    {
        using var workbook = ExcelWorkbook.Open(TestDataFile("complex_workbook.xlsx"));
        using var source = new CancellationTokenSource();
        source.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => workbook.AnalyzeLayoutAsync("Calculator", source.Token));

        Assert.NotEmpty(workbook.AnalyzeLayout("Calculator").MeasureFields);
    }

    [Fact]
    public void AnalyzeLayout_PairedXlsxAndXlsb_ReturnsEquivalentLayout()
    {
        using var xlsx = ExcelWorkbook.Open(TestDataFile("complex_workbook.xlsx"));
        using var xlsb = ExcelWorkbook.Open(TestDataFile("complex_workbook.xlsb"));

        SheetLayoutInfo expected = xlsx.AnalyzeLayout("Calculator");
        SheetLayoutInfo actual = xlsb.AnalyzeLayout("Calculator");

        Assert.NotEmpty(expected.MeasureFields);
        AssertEquivalent(expected, actual);
    }

    [Fact]
    public void AnalyzeLayout_XlsmPath_UsesOpenXmlScanSpi()
    {
        string path = Path.Combine(Path.GetTempPath(), $"xlsight-layout-{Guid.NewGuid():N}.xlsm");
        File.Copy(TestDataFile("complex_workbook.xlsm"), path);
        try
        {
            using var workbook = ExcelWorkbook.Open(path);

            Assert.Equal(WorkbookFormat.Xlsm, workbook.Format);
            Assert.NotEmpty(workbook.AnalyzeLayout("Calculator").MeasureFields);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void InferLayout_Alias_MatchesAnalyzeLayout()
    {
        using var workbook = ExcelWorkbook.Open(TestDataFile("complex_workbook.xlsx"));

        AssertEquivalent(
            workbook.AnalyzeLayout("Calculator"),
            workbook.InferLayout("Calculator"));
    }

    private static string TestDataFile(string fileName) =>
        Path.Combine(AppContext.BaseDirectory, "TestData", fileName);

    private static void AssertEquivalent(SheetLayoutInfo expected, SheetLayoutInfo actual)
    {
        Assert.Equal(
            expected.Axes.Select(AxisSnapshot.Create).OrderBy(static axis => axis.Id, StringComparer.Ordinal).ToArray(),
            actual.Axes.Select(AxisSnapshot.Create).OrderBy(static axis => axis.Id, StringComparer.Ordinal).ToArray());
        Assert.Equal(
            expected.MeasureFields.Select(FieldSnapshot.Create).OrderBy(static field => field.Id, StringComparer.Ordinal).ToArray(),
            actual.MeasureFields.Select(FieldSnapshot.Create).OrderBy(static field => field.Id, StringComparer.Ordinal).ToArray());
        Assert.Equal(
            expected.Groups.Select(GroupSnapshot.Create).OrderBy(static group => group.Id, StringComparer.Ordinal).ToArray(),
            actual.Groups.Select(GroupSnapshot.Create).OrderBy(static group => group.Id, StringComparer.Ordinal).ToArray());
    }

    private readonly record struct AxisSnapshot(
        string Id,
        string? Title,
        LayoutAxisOrientation Orientation,
        LayoutAxisValueKind ValueKind,
        LayoutAxisRole Role,
        ExcelRange Range,
        string Samples,
        string Sections)
    {
        public static AxisSnapshot Create(LayoutAxis axis) => new(
            axis.Id,
            axis.Title,
            axis.Orientation,
            axis.ValueKind,
            axis.Role,
            axis.Range,
            string.Join('\u001f', axis.Samples),
            string.Join('\u001f', axis.Sections.Select(static section => $"{section.Title}:{section.Range}")));
    }

    private readonly record struct FieldSnapshot(
        string Id,
        ExcelRange Range,
        string AxisIds,
        int CellCount,
        int NumericCount,
        int TextCount,
        int FormulaCount,
        double? MinNumeric,
        double? MaxNumeric)
    {
        public static FieldSnapshot Create(MeasureFieldInfo field) => new(
            field.Id,
            field.Range,
            string.Join('\u001f', field.AxisIds),
            field.Profile.CellCount,
            field.Profile.NumericCount,
            field.Profile.TextCount,
            field.Profile.FormulaCount,
            field.Profile.MinNumeric,
            field.Profile.MaxNumeric);
    }

    private readonly record struct GroupSnapshot(
        string Id,
        string? Title,
        ExcelRange Range,
        string AxisIds,
        string MeasureFieldIds)
    {
        public static GroupSnapshot Create(LayoutGroupInfo group) => new(
            group.Id,
            group.Title,
            group.Range,
            string.Join('\u001f', group.AxisIds),
            string.Join('\u001f', group.MeasureFieldIds));
    }
}
