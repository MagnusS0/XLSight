using System.IO.Compression;
using System.Text;
using XLSight.Analysis;
using Xunit;

namespace XLSight.Tests.Analysis;

public sealed class RegionOrientationClassificationTests
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

    // Sheet layout (two tables separated by a blank row gap, gap > VerticalGapTolerance=1):
    //
    //   Crosstab (rows 1-4, cols A-D):
    //     Row 1 (top row): A1="" (label axis label, text), B1="Q4 2025", C1="Q3 2025", D1="Q2 2025"
    //     Row 2: A2="Revenue" (text), B2=100, C2=90, D2=80
    //     Row 3: A3="Cost"    (text), B3=40,  C3=35,  D3=30
    //     Row 4: A4="Profit"  (text), B4=60,  C4=55,  D4=50
    //
    //   Normal table (rows 7-9, cols A-C): 3 blank rows = gap of 2 (>= VerticalGapTolerance+1), sealing crosstab.
    //     Row 7 (header): A7="Product" (text), B7="Units" (text), C7="Price" (text)
    //     Row 8: A8="Widget" (text, shared string), B8=10, C8=5.99
    //     Row 9: A9="Gadget" (text, shared string), B9=20, C9=9.99
    //
    // SST indices: 0=Q4 2025, 1=Q3 2025, 2=Q2 2025, 3=Revenue, 4=Cost, 5=Profit,
    //              6=Product, 7=Units, 8=Price, 9=Widget, 10=Gadget
    private const string SstXml = """
        <sst xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main" uniqueCount="11">
          <si><t>Q4 2025</t></si>
          <si><t>Q3 2025</t></si>
          <si><t>Q2 2025</t></si>
          <si><t>Revenue</t></si>
          <si><t>Cost</t></si>
          <si><t>Profit</t></si>
          <si><t>Product</t></si>
          <si><t>Units</t></si>
          <si><t>Price</t></si>
          <si><t>Widget</t></si>
          <si><t>Gadget</t></si>
        </sst>
        """;

    // Crosstab top row: col A is an empty label cell (omitted), cols B-D have period strings (value-like).
    // Body rows: col A = text label, cols B-D = numeric.
    // Normal table top row: all-text field names; body has text in col A and numeric in B-C.
    private const string SheetXml = """
        <worksheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main">
          <sheetData>
            <row r="1">
              <c r="B1" t="s"><v>0</v></c>
              <c r="C1" t="s"><v>1</v></c>
              <c r="D1" t="s"><v>2</v></c>
            </row>
            <row r="2">
              <c r="A2" t="s"><v>3</v></c>
              <c r="B2"><v>100</v></c>
              <c r="C2"><v>90</v></c>
              <c r="D2"><v>80</v></c>
            </row>
            <row r="3">
              <c r="A3" t="s"><v>4</v></c>
              <c r="B3"><v>40</v></c>
              <c r="C3"><v>35</v></c>
              <c r="D3"><v>30</v></c>
            </row>
            <row r="4">
              <c r="A4" t="s"><v>5</v></c>
              <c r="B4"><v>60</v></c>
              <c r="C4"><v>55</v></c>
              <c r="D4"><v>50</v></c>
            </row>
            <row r="7">
              <c r="A7" t="s"><v>6</v></c>
              <c r="B7" t="s"><v>7</v></c>
              <c r="C7" t="s"><v>8</v></c>
            </row>
            <row r="8">
              <c r="A8" t="s"><v>9</v></c>
              <c r="B8"><v>10</v></c>
              <c r="C8"><v>5.99</v></c>
            </row>
            <row r="9">
              <c r="A9" t="s"><v>10</v></c>
              <c r="B9"><v>20</v></c>
              <c r="C9"><v>9.99</v></c>
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
    public void CrosstabAndNormalTable_ClassifiedByOrientation()
    {
        using var ms = BuildWorkbook(SheetXml, SstXml);
        using var workbook = ExcelWorkbook.Open(ms);

        SheetInfo info = workbook.AnalyzeSheet("Data");
        var inferred = Assert.IsType<SheetAnalysisInferred>(info.Inferred);

        // Expect at least two regions: a crosstab and a data table.
        Assert.True(inferred.Regions.Count >= 2,
            $"Expected at least 2 regions, got {inferred.Regions.Count}: " +
            string.Join(", ", inferred.Regions.Select(r =>
                $"{r.Kind} rows {r.Range.TopLeft.Row}-{r.Range.BottomRight.Row}")));

        // --- Crosstab region (rows 1-4, cols A-D) ---
        // Top row is value-like period strings (topVL >= 0.6), body rows have text-first + numeric-dominant.
        var crosstab = inferred.Regions
            .OrderBy(r => r.Range.TopLeft.Row)
            .FirstOrDefault(r => r.Kind == RegionKind.Crosstab);

        Assert.NotNull(crosstab);
        Assert.Equal(RegionKind.Crosstab, crosstab.Kind);
        // KeyColumnIndex is StartCol of the block (col A = 1).
        Assert.Equal(1, crosstab.KeyColumnIndex);
        Assert.True(crosstab.Confidence > 0, $"Expected Confidence > 0 for Crosstab, got {crosstab.Confidence}");

        // --- Normal table region (rows 7-9, cols A-C) ---
        // Top row is all-text field names (topRowTextRatio=1.0), body is numeric-dominant (bodyNumericRatio >= 0.5).
        var dataTable = inferred.Regions
            .OrderBy(r => r.Range.TopLeft.Row)
            .FirstOrDefault(r => r.Kind == RegionKind.DataTable);

        Assert.NotNull(dataTable);
        Assert.Equal(RegionKind.DataTable, dataTable.Kind);

        // --- Regions must not overlap ---
        Assert.True(
            crosstab.Range.BottomRight.Row < dataTable.Range.TopLeft.Row,
            $"Crosstab and DataTable overlap: crosstab ends row {crosstab.Range.BottomRight.Row}, " +
            $"DataTable starts row {dataTable.Range.TopLeft.Row}");
    }
}
