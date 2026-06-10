using System.Globalization;
using System.IO.Compression;
using System.Text;
using XLSight.Analysis;
using Xunit;

namespace XLSight.Tests.Analysis;

public sealed class DistinctValuesAnalysisTests
{
    private const string WorkbookXml = """
        <workbook xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main"
                  xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships">
          <sheets>
            <sheet name="Data" sheetId="1" r:id="rId1" />
          </sheets>
        </workbook>
        """;

    private const string RelsXml = """
        <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
          <Relationship Id="rId1" Target="worksheets/sheet1.xml" />
        </Relationships>
        """;

    private const string StylesXml = """
        <styleSheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main">
          <cellXfs>
            <xf numFmtId="0" />
          </cellXfs>
        </styleSheet>
        """;

    private const string SstXml = """
        <sst xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main" uniqueCount="3">
          <si><t>EMEA</t></si>
          <si><t>APAC</t></si>
          <si><t>AMER</t></si>
        </sst>
        """;

    /// <summary>
    /// Column A: shared strings cycling EMEA/APAC/AMER (3 distinct).
    /// Column B: a unique number per row (high cardinality when rowCount is large).
    /// </summary>
    private static string BuildSheetXml(int rowCount)
    {
        var sb = new StringBuilder();
        sb.Append("""<worksheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main"><sheetData>""");
        for (int r = 1; r <= rowCount; r++)
        {
            sb.Append(CultureInfo.InvariantCulture, $"""<row r="{r}"><c r="A{r}" t="s"><v>{(r - 1) % 3}</v></c><c r="B{r}"><v>{r * 10}</v></c></row>""");
        }

        sb.Append("</sheetData></worksheet>");
        return sb.ToString();
    }

    private static MemoryStream BuildWorkbook(string sheetXml)
    {
        var ms = new MemoryStream();
        using (var archive = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
        {
            WriteEntry(archive, "xl/workbook.xml", WorkbookXml);
            WriteEntry(archive, "xl/_rels/workbook.xml.rels", RelsXml);
            WriteEntry(archive, "xl/styles.xml", StylesXml);
            WriteEntry(archive, "xl/sharedStrings.xml", SstXml);
            WriteEntry(archive, "xl/worksheets/sheet1.xml", sheetXml);
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
    public void AnalyzeSheet_LowCardinalityColumn_SurfacesDistinctValues()
    {
        using var ms = BuildWorkbook(BuildSheetXml(rowCount: 12));
        using var workbook = ExcelWorkbook.Open(ms);

        SheetInfo info = workbook.AnalyzeSheet("Data");

        var colA = info.Columns!.Single(c => c.ColumnIndex == 1);
        Assert.NotNull(colA.DistinctValues);
        Assert.Equal(["AMER", "APAC", "EMEA"], colA.DistinctValues);
    }

    [Fact]
    public void AnalyzeSheet_HighCardinalityColumn_ReportsOnlyEstimate()
    {
        using var ms = BuildWorkbook(BuildSheetXml(rowCount: 50));
        using var workbook = ExcelWorkbook.Open(ms);

        SheetInfo info = workbook.AnalyzeSheet("Data");

        // 50 distinct numbers exceeds the default cap of 32.
        var colB = info.Columns!.Single(c => c.ColumnIndex == 2);
        Assert.Null(colB.DistinctValues);
        Assert.Equal(50, colB.DistinctValueEstimate);
    }

    [Fact]
    public void AnalyzeSheet_CustomCap_ControlsWhichColumnsSurfaceValues()
    {
        using var ms = BuildWorkbook(BuildSheetXml(rowCount: 50));
        using var workbook = ExcelWorkbook.Open(ms);

        var options = new AnalysisOptions { DistinctValuesCap = 64 };
        SheetInfo info = workbook.AnalyzeSheet("Data", AnalysisLevel.Full, options);

        var colB = info.Columns!.Single(c => c.ColumnIndex == 2);
        Assert.NotNull(colB.DistinctValues);
        Assert.Equal(50, colB.DistinctValues.Count);
        Assert.Equal("10", colB.DistinctValues[0]);
        Assert.Equal("500", colB.DistinctValues[^1]);
    }

    [Fact]
    public void AnalyzeSheet_CapZero_DisablesDistinctValues()
    {
        using var ms = BuildWorkbook(BuildSheetXml(rowCount: 12));
        using var workbook = ExcelWorkbook.Open(ms);

        var options = new AnalysisOptions { DistinctValuesCap = 0 };
        SheetInfo info = workbook.AnalyzeSheet("Data", AnalysisLevel.Full, options);

        Assert.All(info.Columns!, c => Assert.Null(c.DistinctValues));
    }
}
