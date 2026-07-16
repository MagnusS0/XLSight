using XLSight.Analysis;
using Xunit;
using static XLSight.Tests.Analysis.LayoutTestWorkbook;

namespace XLSight.Tests.Analysis;

public sealed class LayoutAxisSampleTests
{
    private const string DateStylesXml = """
        <styleSheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main">
          <cellXfs>
            <xf numFmtId="0" />
            <xf numFmtId="14" applyNumberFormat="1" />
          </cellXfs>
        </styleSheet>
        """;

    private static readonly RowSpec[] TextAxisRows =
    [
        Row(1, Number("B", 2023), Number("C", 2024), Number("D", 2025)),
        Row(2, Text("A", "Revenue"), Number("B", 100), Number("C", 110), Number("D", 120)),
        Row(3, Text("A", "Costs"), Number("B", 40), Number("C", 45), Number("D", 50)),
        Row(4, Text("A", "EBITDA"), Number("B", 60), Number("C", 65), Number("D", 70)),
    ];

    private static readonly RowSpec[] MixedAxisRows =
    [
        Row(1, Text("B", "M1"), Text("C", "M2")),
        Row(2, Text("A", "Start"), Number("B", 1), Number("C", 2)),
        Row(3, Number("A", 45000, styleIndex: 1), Number("B", 3), Number("C", 4)),
        Row(4, Text("A", "End"), Number("B", 5), Number("C", 6)),
        Row(5, Number("A", 45001, styleIndex: 1), Number("B", 7), Number("C", 8)),
    ];

    [Fact]
    public void TextAxis_ExposesTextSamples()
    {
        using var ms = LayoutTestWorkbook.Build(TextAxisRows);
        using var workbook = ExcelWorkbook.Open(ms);

        var inferred = Assert.IsType<SheetAnalysisInferred>(workbook.AnalyzeSheet("Data").Inferred);
        var expectedRange = ExcelRange.Parse("A2:A4");
        LayoutAxis? textAxis = inferred.Layout.Axes.FirstOrDefault(axis =>
            axis.Range == expectedRange &&
            axis.Orientation == LayoutAxisOrientation.Vertical &&
            axis.Role == LayoutAxisRole.Primary);
        Assert.NotNull(textAxis);
        Assert.Equal(["Revenue", "Costs", "EBITDA"], textAxis.Samples);
    }

    [Fact]
    public void TextAfterFormerGlobalBudget_RemainsAvailableForTitlesAndSamples()
    {
        using var ms = LayoutTestWorkbook.Build([.. BuildLateTableRows()]);
        using var workbook = ExcelWorkbook.Open(ms);

        SheetLayoutInfo layout = Assert.IsType<SheetAnalysisInferred>(workbook.AnalyzeSheet("Data").Inferred).Layout;
        LayoutGroupInfo group = Assert.Single(layout.Groups, static group => group.Range.BottomRight.Row == 5014);
        LayoutAxis axis = Assert.Single(layout.Axes, static axis => axis.Range == ExcelRange.Parse("B5012:B5014"));

        Assert.Equal("Revenue Summary", group.Title);
        Assert.Equal(["Revenue", "Costs", "Profit"], axis.Samples);
    }

    [Fact]
    public void TextAndDateAxis_ReportsMixedValueKind()
    {
        using var ms = LayoutTestWorkbook.Build(MixedAxisRows, DateStylesXml);
        using var workbook = ExcelWorkbook.Open(ms);

        SheetLayoutInfo layout = Assert.IsType<SheetAnalysisInferred>(workbook.AnalyzeSheet("Data").Inferred).Layout;
        LayoutAxis axis = Assert.Single(layout.Axes, static axis => axis.Range == ExcelRange.Parse("A2:A5"));

        Assert.Equal(LayoutAxisValueKind.Mixed, axis.ValueKind);
    }

    private static IEnumerable<RowSpec> BuildLateTableRows()
    {
        string[] fillerColumns = ["A", "B", "C", "D", "E", "F", "G", "H", "I", "J"];
        for (int row = 1; row <= 5_000; row++)
        {
            yield return Row(row, [.. fillerColumns.Select(static column => Text(column, "x"))]);
        }

        yield return Row(5010, Text("B", "Revenue Summary"));
        yield return Row(5011, Number("C", 2024), Number("D", 2025));
        yield return Row(5012, Text("B", "Revenue"), Number("C", 10), Number("D", 11));
        yield return Row(5013, Text("B", "Costs"), Number("C", 5), Number("D", 6));
        yield return Row(5014, Text("B", "Profit"), Number("C", 5), Number("D", 5));
    }
}
