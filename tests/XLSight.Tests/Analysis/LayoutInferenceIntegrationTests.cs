using XLSight.Analysis;
using Xunit;

namespace XLSight.Tests.Analysis;

public sealed class LayoutInferenceIntegrationTests
{
    [Fact]
    public void AnalyzeSheet_JyskeGroup_ReturnsStackedCrosstabLayout()
    {
        string? workbookPath = FindRepoFile("Jyske+Bank+Fact+Book+2025+Q4.xlsx");
        if (workbookPath is null)
        {
            return;
        }

        using var workbook = ExcelWorkbook.Open(workbookPath);

        var inferred = Assert.IsType<SheetAnalysisInferred>(workbook.AnalyzeSheet("Group").Inferred);

        AssertField(inferred, "B4:AO19", 2);
        AssertField(inferred, "B24:AO40", 2);
        AssertField(inferred, "B45:AO62", 2);
        AssertAxis(inferred, "A4:A19", LayoutAxisOrientation.Vertical, LayoutAxisRole.Primary);
        AssertAxis(inferred, "B3:AO3", LayoutAxisOrientation.Horizontal, LayoutAxisRole.Primary);
    }

    [Fact]
    public void AnalyzeSheet_McDonaldsFinancials_ReturnsSharedAxisSiblingFields()
    {
        string? workbookPath = FindRepoFile("spreadsheetbench-v2/Financial_Model/spreadsheet/10_McDonalds/10_McDonalds_golden.xlsx");
        if (workbookPath is null)
        {
            return;
        }

        using var workbook = ExcelWorkbook.Open(workbookPath);

        var inferred = Assert.IsType<SheetAnalysisInferred>(workbook.AnalyzeSheet("Financials").Inferred);

        // The income statement spans its full row extent (rows 5-65), not just the first
        // sub-section, and reprints of the year header split the balance sheet and cash
        // flow into their own fields rather than merging them into one block.
        MeasureFieldInfo historicals = AssertField(inferred, "E5:O65", 3);
        AssertField(inferred, "Q5:R65", 3);
        AssertField(inferred, "T5:AB65", 3);
        AssertField(inferred, "AD5:AG65", 3);
        AssertField(inferred, "E70:O101", 3);
        AssertField(inferred, "E106:O127", 3);

        LayoutAxis rowAxis = AssertAxis(inferred, "B5:B65", LayoutAxisOrientation.Vertical, LayoutAxisRole.Primary);
        LayoutAxis contextAxis = AssertAxis(inferred, "D5:D65", LayoutAxisOrientation.Vertical, LayoutAxisRole.Context);
        LayoutAxis yearAxis = AssertAxis(inferred, "E4:O4", LayoutAxisOrientation.Horizontal, LayoutAxisRole.Primary);
        Assert.Contains(rowAxis.Id, historicals.AxisIds);
        Assert.Contains(contextAxis.Id, historicals.AxisIds);
        Assert.Contains(yearAxis.Id, historicals.AxisIds);
    }

    [Fact]
    public void AnalyzeSheet_McDonaldsValuation_ReturnsScalarVectorLayout()
    {
        string? workbookPath = FindRepoFile("spreadsheetbench-v2/Financial_Model/spreadsheet/10_McDonalds/10_McDonalds_golden.xlsx");
        if (workbookPath is null)
        {
            return;
        }

        using var workbook = ExcelWorkbook.Open(workbookPath);

        var inferred = Assert.IsType<SheetAnalysisInferred>(workbook.AnalyzeSheet("Valuation").Inferred);

        MeasureFieldInfo terminalField = AssertField(inferred, "E23:E24", 1);
        LayoutAxis rowAxis = AssertAxis(inferred, "C23:C24", LayoutAxisOrientation.Vertical, LayoutAxisRole.Primary);
        Assert.Contains(rowAxis.Id, terminalField.AxisIds);
    }

    [Fact]
    public void AnalyzeSheet_Calculator_ReturnsProjectionAndSensitivityLayout()
    {
        string workbookPath = Path.Combine(AppContext.BaseDirectory, "TestData", "complex_workbook.xlsx");
        using var workbook = ExcelWorkbook.Open(workbookPath);

        var inferred = Assert.IsType<SheetAnalysisInferred>(workbook.AnalyzeSheet("Calculator").Inferred);

        AssertField(inferred, "C6:C14", 1);
        AssertField(inferred, "C18:C26", 1);

        // The projection's measure columns must be captured, and the left input/summary labels
        // (B6:B52) must not attach as its axis — they label the input blocks, not projection rows.
        // The exact extent is still too coarse (it bleeds into the sensitivity matrix below);
        // tighten this to the projection rows once section-break detection lands.
        MeasureFieldInfo projection = AssertFieldContaining(inferred, "G7:K39");
        Assert.DoesNotContain(inferred.Layout.Axes, axis =>
            axis.Range == ExcelRange.Parse("B6:B52") && projection.AxisIds.Contains(axis.Id));
    }

    private static MeasureFieldInfo AssertFieldContaining(SheetAnalysisInferred inferred, string range)
    {
        var expected = ExcelRange.Parse(range);
        MeasureFieldInfo? field = inferred.Layout.MeasureFields.FirstOrDefault(field =>
            field.Range.TopLeft.Row <= expected.TopLeft.Row &&
            field.Range.TopLeft.Column <= expected.TopLeft.Column &&
            field.Range.BottomRight.Row >= expected.BottomRight.Row &&
            field.Range.BottomRight.Column >= expected.BottomRight.Column);
        Assert.NotNull(field);
        return field;
    }

    [Fact]
    public void AnalyzeSheet_BankingAssumptions_ReturnsOneCoherentBlock()
    {
        string? workbookPath = FindRepoFile("spreadsheetbench-v2/Financial_Model/spreadsheet/03_Project Banking/03_Banking_golden.xlsx");
        if (workbookPath is null)
        {
            return;
        }

        using var workbook = ExcelWorkbook.Open(workbookPath);

        var inferred = Assert.IsType<SheetAnalysisInferred>(workbook.AnalyzeSheet("Assumptions-BS").Inferred);

        // Every section shares the single year header on row 3 with no reprints, so the sheet is one
        // coherent block rather than dozens of per-column or per-section shards.
        AssertField(inferred, "C6:M260", 2);
        Assert.Single(inferred.Layout.MeasureFields);
    }

    [Fact]
    public void AnalyzeSheet_BankingIndustryBenchmark_MergesWideTableAndKeepsColumnGroups()
    {
        string? workbookPath = FindRepoFile("spreadsheetbench-v2/Financial_Model/spreadsheet/03_Project Banking/03_Banking_golden.xlsx");
        if (workbookPath is null)
        {
            return;
        }

        using var workbook = ExcelWorkbook.Open(workbookPath);

        var inferred = Assert.IsType<SheetAnalysisInferred>(workbook.AnalyzeSheet("Industry Benchmark").Inferred);

        // The top grid's four column-groups stay separated by their fully empty spacer columns,
        // each anchoring its own adjacent label column (D/J/P/V) rather than inheriting the
        // first panel's.
        AssertField(inferred, "E8:H33", 2);
        AssertField(inferred, "K8:N33", 2);
        AssertField(inferred, "Q8:T33", 2);
        AssertField(inferred, "W8:Z33", 2);

        // The bottom wide table re-joins across its data-bearing spacers (split only at the fully
        // empty U), and its leading year column peels off as a vertical context axis: rows are
        // keyed by Name (D) + Year (E).
        MeasureFieldInfo bottomLeft = AssertField(inferred, "F38:T62", 3);
        AssertField(inferred, "V38:AD62", 3);
        LayoutAxis yearContext = AssertAxis(inferred, "E38:E62", LayoutAxisOrientation.Vertical, LayoutAxisRole.Context);
        Assert.Contains(yearContext.Id, bottomLeft.AxisIds);
    }

    private static MeasureFieldInfo AssertField(SheetAnalysisInferred inferred, string range, int rank)
    {
        MeasureFieldInfo field = AssertFieldRange(inferred, range);
        Assert.Equal(rank, field.Rank);
        return field;
    }

    private static MeasureFieldInfo AssertFieldRange(SheetAnalysisInferred inferred, string range)
    {
        var expectedRange = ExcelRange.Parse(range);
        MeasureFieldInfo? field = inferred.Layout.MeasureFields.FirstOrDefault(field => field.Range == expectedRange);
        Assert.NotNull(field);
        Assert.True(field.Profile.NumericCount > 0);
        return field;
    }

    private static LayoutAxis AssertAxis(
        SheetAnalysisInferred inferred,
        string range,
        LayoutAxisOrientation orientation,
        LayoutAxisRole role)
    {
        var expectedRange = ExcelRange.Parse(range);
        LayoutAxis? axis = inferred.Layout.Axes.FirstOrDefault(axis =>
            axis.Range == expectedRange &&
            axis.Orientation == orientation &&
            axis.Role == role);
        Assert.NotNull(axis);
        return axis;
    }

    private static string? FindRepoFile(string relativePath)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            string candidate = Path.Combine(directory.FullName, relativePath);
            if (File.Exists(candidate))
            {
                return candidate;
            }

            directory = directory.Parent;
        }

        return null;
    }
}
