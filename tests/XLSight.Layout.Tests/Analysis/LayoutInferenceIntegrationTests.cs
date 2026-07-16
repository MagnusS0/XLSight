using XLSight.Analysis.Layout;
using Xunit;

namespace XLSight.Layout.Tests.Analysis;

// Test workbooks 10_McDonalds_golden.xlsx and 03_Banking_golden.xlsx come from SpreadsheetBench
// v2, an open financial-modeling benchmark dataset.
public sealed class LayoutInferenceIntegrationTests
{
    [Fact]
    public void AnalyzeLayout_McDonaldsFinancials_ReturnsSharedAxisSiblingFields()
    {
        string workbookPath = TestDataFile("10_McDonalds_golden.xlsx");

        using var workbook = ExcelWorkbook.Open(workbookPath);

        var layout = workbook.AnalyzeLayout("Financials");

        // The income statement spans its full row extent (rows 5-65), not just the first
        // sub-section, and reprints of the year header split the balance sheet and cash
        // flow into their own fields rather than merging them into one block.
        MeasureFieldInfo historicals = AssertField(layout, "E5:O65", 3);
        AssertField(layout, "Q5:R65", 3);
        AssertField(layout, "T5:AB65", 3);
        AssertField(layout, "AD5:AG65", 3);
        AssertField(layout, "E70:O101", 3);
        AssertField(layout, "E106:O127", 3);

        LayoutAxis rowAxis = AssertAxis(layout, "B5:B65", LayoutAxisOrientation.Vertical, LayoutAxisRole.Primary);
        LayoutAxis contextAxis = AssertAxis(layout, "D5:D65", LayoutAxisOrientation.Vertical, LayoutAxisRole.Context);
        LayoutAxis yearAxis = AssertAxis(layout, "E4:O4", LayoutAxisOrientation.Horizontal, LayoutAxisRole.Primary);
        Assert.Contains(rowAxis.Id, historicals.AxisIds);
        Assert.Contains(contextAxis.Id, historicals.AxisIds);
        Assert.Contains(yearAxis.Id, historicals.AxisIds);
    }

    [Fact]
    public void AnalyzeLayout_McDonaldsValuation_ReturnsScalarVectorLayout()
    {
        string workbookPath = TestDataFile("10_McDonalds_golden.xlsx");

        using var workbook = ExcelWorkbook.Open(workbookPath);

        var layout = workbook.AnalyzeLayout("Valuation");

        MeasureFieldInfo terminalField = AssertField(layout, "E23:E24", 1);
        LayoutAxis rowAxis = AssertAxis(layout, "C23:C24", LayoutAxisOrientation.Vertical, LayoutAxisRole.Primary);
        Assert.Contains(rowAxis.Id, terminalField.AxisIds);

        // The WACC / terminal-growth sensitivity block is a 2D matrix with numeric coordinate
        // axes of its own — WACC values down G, growth rates across row 59 — and must not
        // borrow labels from the WACC-calculation table to its left.
        MeasureFieldInfo sensitivity = AssertField(layout, "H60:L64", 2);
        LayoutAxis waccAxis = AssertAxis(layout, "G60:G64", LayoutAxisOrientation.Vertical, LayoutAxisRole.Primary);
        LayoutAxis growthAxis = AssertAxis(layout, "H59:L59", LayoutAxisOrientation.Horizontal, LayoutAxisRole.Primary);
        Assert.Contains(waccAxis.Id, sensitivity.AxisIds);
        Assert.Contains(growthAxis.Id, sensitivity.AxisIds);
    }

    [Fact]
    public void AnalyzeLayout_Calculator_ReturnsProjectionAndSensitivityLayout()
    {
        string workbookPath = TestDataFile("complex_workbook.xlsx");
        using var workbook = ExcelWorkbook.Open(workbookPath);

        var layout = workbook.AnalyzeLayout("Calculator");

        AssertField(layout, "C6:C14", 1);
        AssertField(layout, "C18:C26", 1);

        // The projection ends at row 40 (the sensitivity matrix below is its own table), and the
        // left input/summary labels must not attach as its axis — they label the input blocks.
        MeasureFieldInfo projection = AssertField(layout, "E6:K40", 1);
        Assert.DoesNotContain(layout.Axes, axis =>
            axis.Range == ExcelRange.Parse("B6:B52") && projection.AxisIds.Contains(axis.Id, StringComparer.Ordinal));

        // The surplus/shortfall sensitivity block is a 2D matrix: return rates down C,
        // inflation rates across row 44.
        MeasureFieldInfo sensitivity = AssertField(layout, "D46:J52", 2);
        LayoutAxis returnAxis = AssertAxis(layout, "C46:C52", LayoutAxisOrientation.Vertical, LayoutAxisRole.Primary);
        LayoutAxis inflationAxis = AssertAxis(layout, "D44:J44", LayoutAxisOrientation.Horizontal, LayoutAxisRole.Primary);
        Assert.Contains(returnAxis.Id, sensitivity.AxisIds);
        Assert.Contains(inflationAxis.Id, sensitivity.AxisIds);
    }

    [Fact]
    public void AnalyzeLayout_BankingAssumptions_ReturnsOneCoherentBlock()
    {
        string workbookPath = TestDataFile("03_Banking_golden.xlsx");

        using var workbook = ExcelWorkbook.Open(workbookPath);

        var layout = workbook.AnalyzeLayout("Assumptions-BS");

        // Every section shares the single year header on row 3 with no reprints, so the sheet is one
        // coherent block rather than dozens of per-column or per-section shards.
        AssertField(layout, "C6:M260", 2);
        Assert.Single(layout.MeasureFields);

        // The dark-blue no-data header rows inside the label column become axis sections, so a
        // repeated row label like "Total" resolves to its parent (e.g. Total Funding -> Total).
        LayoutAxis labelAxis = AssertAxis(layout, "B6:B260", LayoutAxisOrientation.Vertical, LayoutAxisRole.Primary);
        Assert.Contains(labelAxis.Sections, static section =>
            string.Equals(section.Title, "Total Funding", StringComparison.Ordinal) &&
            section.Range == ExcelRange.Parse("B5:B10"));
        Assert.Contains(labelAxis.Sections, static section =>
            string.Equals(section.Title, "Customer Deposits", StringComparison.Ordinal));
        Assert.Contains(labelAxis.Sections, static section =>
            string.Equals(section.Title, "Gross Loans and Advances", StringComparison.Ordinal));
    }

    [Fact]
    public void AnalyzeLayout_BankingIntroduction_DoesNotGroupAxislessField()
    {
        string workbookPath = TestDataFile("03_Banking_golden.xlsx");
        using var workbook = ExcelWorkbook.Open(workbookPath);

        var layout = workbook.AnalyzeLayout("Introduction");
        MeasureFieldInfo axislessField = AssertField(layout, "T7:T11", 0);

        Assert.Empty(axislessField.AxisIds);
        Assert.DoesNotContain(layout.Groups, group => group.MeasureFieldIds.Any(
            id => string.Equals(id, axislessField.Id, StringComparison.Ordinal)));
        Assert.All(layout.Groups, static group => Assert.NotEmpty(group.AxisIds));
    }

    [Fact]
    public void AnalyzeLayout_BankingIndustryBenchmark_MergesWideTableAndKeepsColumnGroups()
    {
        string workbookPath = TestDataFile("03_Banking_golden.xlsx");

        using var workbook = ExcelWorkbook.Open(workbookPath);

        var layout = workbook.AnalyzeLayout("Industry Benchmark");

        // The top grid's four column-groups stay separated by their fully empty spacer columns,
        // each anchoring its own adjacent label column (D/J/P/V) rather than inheriting the
        // first panel's.
        AssertField(layout, "E8:H33", 2);
        AssertField(layout, "K8:N33", 2);
        AssertField(layout, "Q8:T33", 2);
        AssertField(layout, "W8:Z33", 2);

        // The bottom wide table re-joins across its data-bearing spacers (split only at the fully
        // empty U), and its leading year column peels off as a vertical context axis: rows are
        // keyed by Name (D) + Year (E).
        MeasureFieldInfo bottomLeft = AssertField(layout, "F38:T62", 3);
        AssertField(layout, "V38:AD62", 3);
        LayoutAxis yearContext = AssertAxis(layout, "E38:E62", LayoutAxisOrientation.Vertical, LayoutAxisRole.Context);
        Assert.Contains(yearContext.Id, bottomLeft.AxisIds);
    }

    [Fact]
    public void AnalyzeLayout_BankingFinalOutput_ReturnsTitledDashboardGroups()
    {
        string workbookPath = TestDataFile("03_Banking_golden.xlsx");

        using var workbook = ExcelWorkbook.Open(workbookPath);

        var layout = workbook.AnalyzeLayout("Final Output");

        // A dashboard of small titled tables: each keeps its own title-row group rather than
        // merging into one sheet-wide block.
        Assert.Contains(layout.Groups, static g => string.Equals(g.Title, "Valuation Multiples", StringComparison.Ordinal) && g.Range == ExcelRange.Parse("C62:E68"));
        Assert.Contains(layout.Groups, static g => string.Equals(g.Title, "Growth & Efficiency Ratios", StringComparison.Ordinal) && g.Range == ExcelRange.Parse("C71:E79"));
        Assert.Contains(layout.Groups, static g => string.Equals(g.Title, "Financial Summary", StringComparison.Ordinal) && g.Range == ExcelRange.Parse("C91:M103"));
        Assert.Contains(layout.Groups, static g => string.Equals(g.Title, "SAR", StringComparison.Ordinal) && g.Range == ExcelRange.Parse("C7:E10"));
        Assert.Contains(layout.Groups, static g => string.Equals(g.Title, "Stock Facts", StringComparison.Ordinal) && g.Range == ExcelRange.Parse("C14:E17"));
        Assert.Contains(layout.Groups, static g => string.Equals(g.Title, "Stock Performance (%)", StringComparison.Ordinal) && g.Range == ExcelRange.Parse("C20:E22"));
        Assert.Contains(layout.Groups, static g => string.Equals(g.Title, "Valuation - 2014F", StringComparison.Ordinal) && g.Range == ExcelRange.Parse("C25:E34"));

        MeasureFieldInfo financialSummary = AssertField(layout, "H93:M103", 2);
        LayoutAxis labelAxis = AssertAxis(layout, "C93:C103", LayoutAxisOrientation.Vertical, LayoutAxisRole.Primary);
        Assert.Contains(labelAxis.Id, financialSummary.AxisIds);
    }

    [Fact]
    public void AnalyzeLayout_BankingBalanceSheet_ExtendsMainFieldPastFormerPhantomMatrix()
    {
        string workbookPath = TestDataFile("03_Banking_golden.xlsx");

        using var workbook = ExcelWorkbook.Open(workbookPath);

        var layout = workbook.AnalyzeLayout("Balance Sheet");

        // The main statement runs its full row extent (5-53) in one field: a uniform-stepped
        // forecast row (I9:M9, CAGR-driven model output) no longer seeds a phantom sensitivity
        // matrix at J10:M12 that used to truncate the field at row 8.
        AssertField(layout, "C5:M53", 2);
        Assert.DoesNotContain(layout.MeasureFields, static f => f.Range == ExcelRange.Parse("J10:M12"));

        // The three ratio blocks (CAGR/Growth/Composition) are column-siblings of the main
        // statement's header row. Once that header's field no longer splits at row 8, sibling-row
        // anchoring extends all three to the same row span and their shared label axis merges all
        // four fields into one group — an accepted over-merge rather than leaving the table
        // fragmented. Their individual captions aren't lost, though: each side block's own
        // horizontal header axis still picks up its merged caption cell as a title.
        LayoutGroupInfo group = Assert.Single(layout.Groups);
        Assert.Equal(ExcelRange.Parse("B3:AL53"), group.Range);
        Assert.Equal(4, group.MeasureFieldIds.Count);

        AssertField(layout, "O5:P53", 2);
        AssertField(layout, "R5:AA53", 2);
        AssertField(layout, "AC5:AL53", 2);
        LayoutAxis labelAxis = AssertAxis(layout, "B5:B53", LayoutAxisOrientation.Vertical, LayoutAxisRole.Primary);
        Assert.All(layout.MeasureFields, field => Assert.Contains(labelAxis.Id, field.AxisIds));

        LayoutAxis cagrHeader = AssertAxis(layout, "O3:P3", LayoutAxisOrientation.Horizontal, LayoutAxisRole.Primary);
        LayoutAxis growthHeader = AssertAxis(layout, "R3:AA3", LayoutAxisOrientation.Horizontal, LayoutAxisRole.Primary);
        LayoutAxis compositionHeader = AssertAxis(layout, "AC3:AL3", LayoutAxisOrientation.Horizontal, LayoutAxisRole.Primary);
        Assert.Equal("CAGR (%)", cagrHeader.Title);
        Assert.Equal("Growth (%)", growthHeader.Title);
        Assert.Equal("COMPOSITION OF BALANCE SHEET", compositionHeader.Title);
    }

    private static string TestDataFile(string fileName) => Path.Combine(AppContext.BaseDirectory, "TestData", fileName);

    private static MeasureFieldInfo AssertField(SheetLayoutInfo layout, string range, int rank)
    {
        MeasureFieldInfo field = AssertFieldRange(layout, range);
        Assert.Equal(rank, field.Rank);
        return field;
    }

    private static MeasureFieldInfo AssertFieldRange(SheetLayoutInfo layout, string range)
    {
        var expectedRange = ExcelRange.Parse(range);
        MeasureFieldInfo? field = layout.MeasureFields.FirstOrDefault(field => field.Range == expectedRange);
        Assert.NotNull(field);
        Assert.True(field.Profile.NumericCount > 0);
        return field;
    }

    private static LayoutAxis AssertAxis(
        SheetLayoutInfo layout,
        string range,
        LayoutAxisOrientation orientation,
        LayoutAxisRole role)
    {
        var expectedRange = ExcelRange.Parse(range);
        LayoutAxis? axis = layout.Axes.FirstOrDefault(axis =>
            axis.Range == expectedRange &&
            axis.Orientation == orientation &&
            axis.Role == role);
        Assert.NotNull(axis);
        return axis;
    }
}
