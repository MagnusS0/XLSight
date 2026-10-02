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
            <xf numFmtId="14" />
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

    private static MemoryStream BuildWorkbook(string sheetXml, string sstXml = SstXml)
    {
        var ms = new MemoryStream();
        using (var archive = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
        {
            WriteEntry(archive, "xl/workbook.xml", WorkbookXml);
            WriteEntry(archive, "xl/_rels/workbook.xml.rels", RelsXml);
            WriteEntry(archive, "xl/styles.xml", StylesXml);
            WriteEntry(archive, "xl/sharedStrings.xml", sstXml);
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
        Assert.Equal(12, colA.NonEmptyCount);
        Assert.Equal(12, colA.TextCount);
        Assert.Equal(4, colA.MaxTextLength);
    }

    [Theory]
    [InlineData(32, null, true)]
    [InlineData(33, null, false)]
    [InlineData(50, 64, true)]
    [InlineData(12, 0, false)]
    public void AnalyzeSheet_OutputCap_ControlsDistinctValues(int rowCount, int? cap, bool expectValues)
    {
        using var ms = BuildWorkbook(BuildSheetXml(rowCount));
        using var workbook = ExcelWorkbook.Open(ms);

        AnalysisOptions? options = cap is { } limit ? new AnalysisOptions { DistinctValuesCap = limit } : null;
        SheetInfo info = workbook.AnalyzeSheet("Data", AnalysisLevel.Full, options);

        var colB = info.Columns!.Single(c => c.ColumnIndex == 2);
        Assert.Equal(rowCount, colB.DistinctValueEstimate);
        if (expectValues)
        {
            Assert.Equal(
                Enumerable.Range(1, rowCount).Select(r => (r * 10).ToString(CultureInfo.InvariantCulture)),
                colB.DistinctValues);
        }
        else
        {
            Assert.Null(colB.DistinctValues);
        }

        if (cap == 0)
        {
            Assert.All(info.Columns!, c => Assert.Null(c.DistinctValues));
        }
    }

    private const string _typedSheetXml = """
        <worksheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main"><sheetData>
          <row r="1">
            <c r="A1"><v>10</v></c><c r="B1" t="inlineStr"><is><t>short</t></is></c>
            <c r="C1" s="1"><v>45352</v></c><c r="D1" t="s"><v>0</v></c>
            <c r="E1" t="e"><v>#REF!</v></c><c r="F1"><v>42</v></c>
          </row>
          <row r="2">
            <c r="A2"><v>3</v></c><c r="B2" t="inlineStr"><is><t>a much longer string</t></is></c>
            <c r="C2" s="1"><v>45352</v></c><c r="D2" t="s"><v>1</v></c>
            <c r="E2" t="e"><v>#VALUE!</v></c><c r="F2" />
          </row>
          <row r="3">
            <c r="A3"><v>7</v></c><c r="B3" />
            <c r="C3" s="1"><v>45352.5</v></c><c r="D3" t="s"><v>1</v></c>
            <c r="E3"><v>7</v></c>
          </row>
        </sheetData></worksheet>
        """;

    private const string _typedSstXml = """
        <sst xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main" uniqueCount="2">
          <si><t>hi</t></si><si><t>longer string here</t></si>
        </sst>
        """;

    [Fact]
    public void AnalyzeSheet_TypedColumns_ExposeCountsBoundsAndDistinctValues()
    {
        using var ms = BuildWorkbook(_typedSheetXml, _typedSstXml);
        using var workbook = ExcelWorkbook.Open(ms);

        var columns = workbook.AnalyzeSheet("Data").Columns!.ToDictionary(c => c.ColumnIndex);

        Assert.Equal(6, columns.Count);
        Assert.Equal(CellType.Number, columns[1].DominantType);
        Assert.Equal(3, columns[1].NumberCount);
        Assert.Equal(3, columns[1].NonEmptyCount);
        Assert.Equal(3, columns[1].MinNumericValue);
        Assert.Equal(10, columns[1].MaxNumericValue);
        Assert.Equal(3, columns[1].DistinctValueEstimate);
        Assert.Equal(["3", "7", "10"], columns[1].DistinctValues);

        Assert.Equal(CellType.Text, columns[2].DominantType);
        Assert.Equal(2, columns[2].TextCount);
        Assert.Equal(2, columns[2].NonEmptyCount);
        Assert.Equal(20, columns[2].MaxTextLength);
        Assert.Equal(["a much longer string", "short"], columns[2].DistinctValues);
        Assert.Null(columns[2].MinNumericValue);
        Assert.Null(columns[2].MaxNumericValue);

        Assert.Equal(CellType.Date, columns[3].DominantType);
        Assert.Equal(3, columns[3].DateCount);
        Assert.Equal(3, columns[3].NonEmptyCount);
        Assert.Equal(2, columns[3].DistinctValueEstimate);
        Assert.Equal(["2024-03-01", "2024-03-01T12:00:00"], columns[3].DistinctValues);

        Assert.Equal(CellType.Text, columns[4].DominantType);
        Assert.Equal(3, columns[4].TextCount);
        Assert.Equal(3, columns[4].NonEmptyCount);
        Assert.Equal(18, columns[4].MaxTextLength);
        Assert.Equal(["hi", "longer string here"], columns[4].DistinctValues);

        // Two errors must outweigh the one number; counting only one error would tie
        // with Number and incorrectly make it the dominant type.
        Assert.Equal(CellType.Error, columns[5].DominantType);
        Assert.Equal(3, columns[5].NonEmptyCount);
        Assert.Equal(1, columns[5].NumberCount);

        Assert.Equal(1, columns[6].NonEmptyCount);
        Assert.Equal(1, columns[6].NumberCount);
        Assert.Equal(42, columns[6].MinNumericValue);
        Assert.Equal(42, columns[6].MaxNumericValue);
    }

    [Theory]
    [InlineData(1, 1, new[] { "TRUE" })]
    [InlineData(0, 0, new[] { "FALSE" })]
    [InlineData(1, 0, new[] { "FALSE", "TRUE" })]
    public void AnalyzeSheet_Booleans_CountsDistinctValues(int first, int second, string[] expected)
    {
        string sheetXml = FormattableString.Invariant($"""
            <worksheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main"><sheetData>
              <row r="1"><c r="A1" t="b"><v>{first}</v></c></row>
              <row r="2"><c r="A2" t="b"><v>{second}</v></c></row>
            </sheetData></worksheet>
            """);
        using var ms = BuildWorkbook(sheetXml);
        using var workbook = ExcelWorkbook.Open(ms);

        ColumnProfile column = Assert.Single(workbook.AnalyzeSheet("Data").Columns!);

        Assert.Equal(CellType.Boolean, column.DominantType);
        Assert.Equal(2, column.NonEmptyCount);
        Assert.Equal(2, column.BooleanCount);
        Assert.Equal(expected.Length, column.DistinctValueEstimate);
        Assert.Equal(expected, column.DistinctValues);
    }

    [Theory]
    [InlineData(32, true)]
    [InlineData(7, false)]
    public void AnalyzeSheet_MixedKinds_DeduplicatesTextsAndAppliesCombinedCap(int cap, bool expectValues)
    {
        const string sheetXml = """
            <worksheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main"><sheetData>
              <row r="1"><c r="A1"><v>7.5</v></c></row>
              <row r="2"><c r="A2" t="s"><v>0</v></c></row>
              <row r="3"><c r="A3" t="inlineStr"><is><t>EMEA</t></is></c></row>
              <row r="4"><c r="A4" t="inlineStr"><is><t>APAC</t></is></c></row>
              <row r="5"><c r="A5"><v>-2</v></c></row>
              <row r="6"><c r="A6" s="1"><v>45352.5</v></c></row>
              <row r="7"><c r="A7" s="1"><v>45352</v></c></row>
              <row r="8"><c r="A8" t="b"><v>1</v></c></row>
              <row r="9"><c r="A9" t="b"><v>0</v></c></row>
            </sheetData></worksheet>
            """;
        using var ms = BuildWorkbook(sheetXml);
        using var workbook = ExcelWorkbook.Open(ms);

        var options = new AnalysisOptions { DistinctValuesCap = cap };
        ColumnProfile column = Assert.Single(workbook.AnalyzeSheet("Data", AnalysisLevel.Full, options).Columns!);

        if (expectValues)
        {
            Assert.Equal(
                ["APAC", "EMEA", "-2", "7.5", "2024-03-01", "2024-03-01T12:00:00", "FALSE", "TRUE"],
                column.DistinctValues);
        }
        else
        {
            Assert.Null(column.DistinctValues);
        }
    }

    [Theory]
    [InlineData("number", CellType.Number)]
    [InlineData("date", CellType.Date)]
    [InlineData("inline", CellType.Text)]
    [InlineData("shared", CellType.Text)]
    public void AnalyzeSheet_TrackingLimit_KeepsCountsButCannotRecoverExactValues(string kind, CellType type)
    {
        const int rowCount = 1100;
        var sheet = new StringBuilder("""<worksheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main"><sheetData>""");
        var sst = new StringBuilder("""<sst xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main">""");
        for (int row = 1; row <= rowCount; row++)
        {
            string cell = kind switch
            {
                "number" => FormattableString.Invariant($"""<c r="A{row}"><v>{row}</v></c>"""),
                "date" => FormattableString.Invariant($"""<c r="A{row}" s="1"><v>{45000 + row}</v></c>"""),
                "inline" => FormattableString.Invariant($"""<c r="A{row}" t="inlineStr"><is><t>value{row}</t></is></c>"""),
                "shared" => FormattableString.Invariant($"""<c r="A{row}" t="s"><v>{row - 1}</v></c>"""),
                _ => throw new ArgumentOutOfRangeException(nameof(kind)),
            };
            sheet.Append(CultureInfo.InvariantCulture, $"""<row r="{row}">{cell}</row>""");
            sst.Append(CultureInfo.InvariantCulture, $"<si><t>value{row}</t></si>");
        }
        sheet.Append("</sheetData></worksheet>");
        sst.Append("</sst>");

        using var ms = BuildWorkbook(sheet.ToString(), sst.ToString());
        using var workbook = ExcelWorkbook.Open(ms);
        var options = new AnalysisOptions { DistinctValuesCap = int.MaxValue };

        ColumnProfile column = Assert.Single(workbook.AnalyzeSheet("Data", AnalysisLevel.Full, options).Columns!);

        Assert.Equal(type, column.DominantType);
        Assert.Equal(rowCount, column.NonEmptyCount);
        Assert.Equal(type == CellType.Number ? rowCount : 0, column.NumberCount);
        Assert.Equal(type == CellType.Date ? rowCount : 0, column.DateCount);
        Assert.Equal(type == CellType.Text ? rowCount : 0, column.TextCount);
        Assert.InRange(column.DistinctValueEstimate, 1000, rowCount);
        Assert.Null(column.DistinctValues);
        if (type == CellType.Number)
        {
            Assert.Equal(1, column.MinNumericValue);
            Assert.Equal(rowCount, column.MaxNumericValue);
        }
    }
}
