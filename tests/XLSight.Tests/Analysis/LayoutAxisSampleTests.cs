using System.IO.Compression;
using System.Text;
using XLSight.Analysis;
using Xunit;

namespace XLSight.Tests.Analysis;

public sealed class LayoutAxisSampleTests
{
    private const string StylesXmlDefault = """
        <styleSheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main">
          <cellXfs>
            <xf numFmtId="0" />
          </cellXfs>
        </styleSheet>
        """;

    private const string WorkbookXmlOneSheet = """
        <workbook xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main"
                  xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships">
          <sheets>
            <sheet name="Data" sheetId="1" r:id="rId1" />
          </sheets>
        </workbook>
        """;

    private const string RelsXmlOneSheet = """
        <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
          <Relationship Id="rId1" Target="worksheets/sheet1.xml" />
        </Relationships>
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

    private static MemoryStream BuildWorkbook(string sheetXml, string sstXml)
    {
        var ms = new MemoryStream();
        using (var archive = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
        {
            WriteEntry(archive, "xl/workbook.xml", WorkbookXmlOneSheet);
            WriteEntry(archive, "xl/_rels/workbook.xml.rels", RelsXmlOneSheet);
            WriteEntry(archive, "xl/styles.xml", StylesXmlDefault);
            WriteEntry(archive, "xl/worksheets/sheet1.xml", sheetXml);
            WriteEntry(archive, "xl/sharedStrings.xml", sstXml);
        }

        ms.Position = 0;
        return ms;
    }

    private static void WriteEntry(ZipArchive archive, string path, string content)
    {
        var entry = archive.CreateEntry(path);
        using var stream = entry.Open();
        using var writer = new StreamWriter(stream, Encoding.UTF8, leaveOpen: true);
        writer.Write(content);
    }

    [Fact]
    public void TextAxis_ExposesTextSamples()
    {
        using var ms = BuildWorkbook(SheetXml, SstXml);
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
}
