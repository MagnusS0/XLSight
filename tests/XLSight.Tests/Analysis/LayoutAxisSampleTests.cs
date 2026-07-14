using System.Text;
using XLSight.Analysis;
using Xunit;

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

    // Row 1 (header): year labels anchor the horizontal axis.
    // Rows 2-4: col A carries text labels (the vertical axis), cols B-D carry numeric data.
    // SST: 0=Revenue, 1=Costs, 2=EBITDA.
    private const string SstXml = """
        <sst xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main" uniqueCount="3">
          <si><t>Revenue</t></si>
          <si><t>Costs</t></si>
          <si><t>EBITDA</t></si>
        </sst>
        """;

    private const string SheetXml = """
        <worksheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main">
          <sheetData>
            <row r="1">
              <c r="B1"><v>2023</v></c>
              <c r="C1"><v>2024</v></c>
              <c r="D1"><v>2025</v></c>
            </row>
            <row r="2">
              <c r="A2" t="s"><v>0</v></c>
              <c r="B2"><v>100</v></c>
              <c r="C2"><v>110</v></c>
              <c r="D2"><v>120</v></c>
            </row>
            <row r="3">
              <c r="A3" t="s"><v>1</v></c>
              <c r="B3"><v>40</v></c>
              <c r="C3"><v>45</v></c>
              <c r="D3"><v>50</v></c>
            </row>
            <row r="4">
              <c r="A4" t="s"><v>2</v></c>
              <c r="B4"><v>60</v></c>
              <c r="C4"><v>65</v></c>
              <c r="D4"><v>70</v></c>
            </row>
          </sheetData>
        </worksheet>
        """;

    [Fact]
    public void TextAxis_ExposesTextSamples()
    {
        using var ms = LayoutTestWorkbook.Build(SheetXml, SstXml);
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
        const string sstXml = """
            <sst xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main" uniqueCount="5">
              <si><t>x</t></si>
              <si><t>Revenue Summary</t></si>
              <si><t>Revenue</t></si>
              <si><t>Costs</t></si>
              <si><t>Profit</t></si>
            </sst>
            """;

        using var ms = LayoutTestWorkbook.Build(BuildLateTableSheetXml(), sstXml);
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
        const string sstXml = """
            <sst xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main" uniqueCount="4">
              <si><t>M1</t></si>
              <si><t>M2</t></si>
              <si><t>Start</t></si>
              <si><t>End</t></si>
            </sst>
            """;
        const string sheetXml = """
            <worksheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main">
              <sheetData>
                <row r="1">
                  <c r="B1" t="s"><v>0</v></c>
                  <c r="C1" t="s"><v>1</v></c>
                </row>
                <row r="2">
                  <c r="A2" t="s"><v>2</v></c>
                  <c r="B2"><v>1</v></c>
                  <c r="C2"><v>2</v></c>
                </row>
                <row r="3">
                  <c r="A3" s="1"><v>45000</v></c>
                  <c r="B3"><v>3</v></c>
                  <c r="C3"><v>4</v></c>
                </row>
                <row r="4">
                  <c r="A4" t="s"><v>3</v></c>
                  <c r="B4"><v>5</v></c>
                  <c r="C4"><v>6</v></c>
                </row>
                <row r="5">
                  <c r="A5" s="1"><v>45001</v></c>
                  <c r="B5"><v>7</v></c>
                  <c r="C5"><v>8</v></c>
                </row>
              </sheetData>
            </worksheet>
            """;

        using var ms = LayoutTestWorkbook.Build(sheetXml, sstXml, DateStylesXml);
        using var workbook = ExcelWorkbook.Open(ms);

        SheetLayoutInfo layout = Assert.IsType<SheetAnalysisInferred>(workbook.AnalyzeSheet("Data").Inferred).Layout;
        LayoutAxis axis = Assert.Single(layout.Axes, static axis => axis.Range == ExcelRange.Parse("A2:A5"));

        Assert.Equal(LayoutAxisValueKind.Mixed, axis.ValueKind);
    }

    private static string BuildLateTableSheetXml()
    {
        string[] fillerColumns = ["A", "B", "C", "D", "E", "F", "G", "H", "I", "J"];
        var xml = new StringBuilder("<worksheet xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\"><sheetData>");
        for (int row = 1; row <= 5_000; row++)
        {
            xml.Append("<row r=\"").Append(row).Append("\">");
            foreach (string column in fillerColumns)
            {
                xml.Append("<c r=\"").Append(column).Append(row).Append("\" t=\"s\"><v>0</v></c>");
            }

            xml.Append("</row>");
        }

        xml.Append("""
            <row r="5010"><c r="B5010" t="s"><v>1</v></c></row>
            <row r="5011"><c r="C5011"><v>2024</v></c><c r="D5011"><v>2025</v></c></row>
            <row r="5012"><c r="B5012" t="s"><v>2</v></c><c r="C5012"><v>10</v></c><c r="D5012"><v>11</v></c></row>
            <row r="5013"><c r="B5013" t="s"><v>3</v></c><c r="C5013"><v>5</v></c><c r="D5013"><v>6</v></c></row>
            <row r="5014"><c r="B5014" t="s"><v>4</v></c><c r="C5014"><v>5</v></c><c r="D5014"><v>5</v></c></row>
            </sheetData></worksheet>
            """);
        return xml.ToString();
    }
}
