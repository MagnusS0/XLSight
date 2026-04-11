using System.IO.Compression;
using System.Text;
using XLSight.Analysis;
using Xunit;

namespace XLSight.Tests.Analysis;

public sealed class WorkbookAnalysisTests
{
    // --- Shared XML fragments ---

    private static string TestFilePath(string fileName) =>
        Path.Combine(AppContext.BaseDirectory, "TestData", fileName);

    private const string RelsXmlTwoSheets = """
        <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
          <Relationship Id="rId1" Target="worksheets/sheet1.xml" />
          <Relationship Id="rId2" Target="worksheets/sheet2.xml" />
        </Relationships>
        """;

    private const string RelsXmlOneSheet = """
        <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
          <Relationship Id="rId1" Target="worksheets/sheet1.xml" />
        </Relationships>
        """;

    private const string StylesXmlDefault = """
        <styleSheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main">
          <cellXfs>
            <xf numFmtId="0" />
          </cellXfs>
        </styleSheet>
        """;

    private const string EmptySheetXml = """
        <worksheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main">
          <sheetData />
        </worksheet>
        """;

    private const string WorkbookXmlTwoSheets = """
        <workbook xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main"
                  xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships">
          <sheets>
            <sheet name="Sheet1" sheetId="1" r:id="rId1" />
            <sheet name="Sheet2" sheetId="2" r:id="rId2" />
          </sheets>
        </workbook>
        """;

    private const string WorkbookXmlOneSheet = """
        <workbook xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main"
                  xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships">
          <sheets>
            <sheet name="Data" sheetId="1" r:id="rId1" />
          </sheets>
        </workbook>
        """;

    private const string WorkbookXmlWithNamedRange = """
        <workbook xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main"
                  xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships">
          <sheets>
            <sheet name="Data" sheetId="1" r:id="rId1" />
          </sheets>
          <definedNames>
            <definedName name="MyRange">Data!$A$1:$B$5</definedName>
          </definedNames>
        </workbook>
        """;

    private const string SheetXmlA1B2Data = """
        <worksheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main">
          <dimension ref="A1:B2" />
          <sheetData>
            <row r="1">
              <c r="A1"><v>1</v></c>
              <c r="B1"><v>2</v></c>
            </row>
            <row r="2">
              <c r="A2"><v>3</v></c>
              <c r="B2"><v>4</v></c>
            </row>
          </sheetData>
        </worksheet>
        """;

    private const string SheetXmlNumericColumn = """
        <worksheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main">
          <sheetData>
            <row r="1">
              <c r="A1"><v>10</v></c>
            </row>
            <row r="2">
              <c r="A2"><v>20</v></c>
            </row>
            <row r="3">
              <c r="A3"><v>30</v></c>
            </row>
          </sheetData>
        </worksheet>
        """;

    private const string SheetXmlHeaderThenData = """
        <worksheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main">
          <sheetData>
            <row r="1">
              <c r="A1" t="s"><v>0</v></c>
              <c r="B1" t="s"><v>1</v></c>
            </row>
            <row r="2">
              <c r="A2"><v>42</v></c>
              <c r="B2"><v>3.14</v></c>
            </row>
          </sheetData>
        </worksheet>
        """;

    private const string SstXmlNameScore = """
        <sst xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main" uniqueCount="2">
          <si><t>Name</t></si>
          <si><t>Score</t></si>
        </sst>
        """;

    private const string SheetXmlWithMerge = """
        <worksheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main">
          <sheetData>
            <row r="1">
              <c r="A1"><v>1</v></c>
            </row>
          </sheetData>
          <mergeCells>
            <mergeCell ref="A1:B2" />
          </mergeCells>
        </worksheet>
        """;

    // --- Helpers ---

    private static void WriteEntry(ZipArchive archive, string path, string content)
    {
        var entry = archive.CreateEntry(path);
        using var stream = entry.Open();
        using var writer = new StreamWriter(stream, Encoding.UTF8, leaveOpen: true);
        writer.Write(content);
    }

    private static MemoryStream BuildWorkbook(
        string workbookXml,
        string relsXml,
        string sheetXml,
        string? sheet2Xml = null,
        string? sstXml = null,
        string? stylesXml = null)
    {
        var ms = new MemoryStream();
        using (var archive = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
        {
            WriteEntry(archive, "xl/workbook.xml", workbookXml);
            WriteEntry(archive, "xl/_rels/workbook.xml.rels", relsXml);
            WriteEntry(archive, "xl/styles.xml", stylesXml ?? StylesXmlDefault);
            WriteEntry(archive, "xl/worksheets/sheet1.xml", sheetXml);
            if (sheet2Xml is not null)
            {
                WriteEntry(archive, "xl/worksheets/sheet2.xml", sheet2Xml);
            }
            if (sstXml is not null)
            {
                WriteEntry(archive, "xl/sharedStrings.xml", sstXml);
            }
        }
        ms.Position = 0;
        return ms;
    }

    // --- Tests ---

    [Fact]
    public void Analyze_MultipleSheets_ReturnsCorrectSheetCount()
    {
        using var ms = BuildWorkbook(WorkbookXmlTwoSheets, RelsXmlTwoSheets, EmptySheetXml, EmptySheetXml);
        using var workbook = ExcelWorkbook.Open(ms);

        WorkbookInfo info = workbook.Analyze();

        Assert.Equal(2, info.Sheets.Count);
        Assert.Equal("Sheet1", info.Sheets[0].SheetName);
        Assert.Equal("Sheet2", info.Sheets[1].SheetName);
    }

    [Fact]
    public void Analyze_BasicProperties_MatchWorkbookMetadata()
    {
        using var ms = BuildWorkbook(WorkbookXmlWithNamedRange, RelsXmlOneSheet, EmptySheetXml);
        using var workbook = ExcelWorkbook.Open(ms);

        WorkbookInfo info = workbook.Analyze();

        Assert.False(info.HasMacros);
        Assert.False(info.IsDate1904);
        Assert.Single(info.NamedRanges);
        Assert.Equal("MyRange", info.NamedRanges[0].Name);
        Assert.Equal("Data!$A$1:$B$5", info.NamedRanges[0].Reference);
    }

    [Fact]
    public void AnalyzeSheet_WithData_ReturnsUsedRange()
    {
        using var ms = BuildWorkbook(WorkbookXmlOneSheet, RelsXmlOneSheet, SheetXmlA1B2Data);
        using var workbook = ExcelWorkbook.Open(ms);

        SheetInfo info = workbook.AnalyzeSheet("Data");

        Assert.False(info.IsEmpty);
        Assert.Equal(2, info.RowCount);
        Assert.Equal(2, info.ColumnCount);
        Assert.Equal(4, info.CellCount);
        Assert.NotNull(info.UsedRange);
    }

    [Fact]
    public void AnalyzeSheet_EmptySheet_IsEmpty()
    {
        using var ms = BuildWorkbook(WorkbookXmlOneSheet, RelsXmlOneSheet, EmptySheetXml);
        using var workbook = ExcelWorkbook.Open(ms);

        SheetInfo info = workbook.AnalyzeSheet("Data");

        Assert.True(info.IsEmpty);
        Assert.Equal(0, info.CellCount);
        Assert.Null(info.UsedRange);
    }

    [Fact]
    public void AnalyzeSheet_NumericColumn_DominantTypeIsNumber()
    {
        using var ms = BuildWorkbook(WorkbookXmlOneSheet, RelsXmlOneSheet, SheetXmlNumericColumn);
        using var workbook = ExcelWorkbook.Open(ms);

        SheetInfo info = workbook.AnalyzeSheet("Data");

        Assert.Single(info.Columns);
        Assert.Equal(CellType.Number, info.Columns[0].DominantType);
        Assert.Equal(3, info.Columns[0].NonEmptyCount);
    }

    [Fact]
    public void AnalyzeSheet_TextFirstRowThenData_InfersHeaderRow()
    {
        using var ms = BuildWorkbook(
            WorkbookXmlOneSheet, RelsXmlOneSheet, SheetXmlHeaderThenData, sstXml: SstXmlNameScore);
        using var workbook = ExcelWorkbook.Open(ms);

        SheetInfo info = workbook.AnalyzeSheet("Data");

        Assert.Equal(1, info.InferredHeaderRowIndex);

        var colA = info.Columns.FirstOrDefault(c => c.ColumnIndex == 1);
        Assert.NotNull(colA);
        Assert.Equal("Name", colA.InferredHeader);
    }

    [Fact]
    public void AnalyzeSheet_MergedRegions_CollectedCorrectly()
    {
        using var ms = BuildWorkbook(WorkbookXmlOneSheet, RelsXmlOneSheet, SheetXmlWithMerge);
        using var workbook = ExcelWorkbook.Open(ms);

        SheetInfo info = workbook.AnalyzeSheet("Data");

        Assert.Single(info.MergedRegions);
        var region = info.MergedRegions[0];
        Assert.Equal(1, region.StartRow);
        Assert.Equal(1, region.StartColumn);
        Assert.Equal(2, region.EndRow);
        Assert.Equal(2, region.EndColumn);
    }

    [Fact]
    public void AnalyzeSheet_ExactLevel_ReturnsOnlyExactMetadata()
    {
        using var ms = BuildWorkbook(WorkbookXmlOneSheet, RelsXmlOneSheet, SheetXmlWithMerge);
        using var workbook = ExcelWorkbook.Open(ms);

        SheetInfo info = workbook.AnalyzeSheet("Data", AnalysisLevel.Exact);

        Assert.Equal(AnalysisLevel.Exact, info.Level);
        Assert.False(info.HasObserved);
        Assert.False(info.HasInferred);
        Assert.Single(info.MergedRegions);
        Assert.Throws<InvalidOperationException>(() => _ = info.RowCount);
        Assert.Throws<InvalidOperationException>(() => _ = info.InferredHeaderRowIndex);
    }

    [Fact]
    public void AnalyzeSheet_ObservedLevel_ReturnsObservedWithoutInference()
    {
        using var ms = BuildWorkbook(
            WorkbookXmlOneSheet, RelsXmlOneSheet, SheetXmlHeaderThenData, sstXml: SstXmlNameScore);
        using var workbook = ExcelWorkbook.Open(ms);

        SheetInfo info = workbook.AnalyzeSheet("Data", AnalysisLevel.Observed);

        Assert.Equal(AnalysisLevel.Observed, info.Level);
        Assert.True(info.HasObserved);
        Assert.False(info.HasInferred);
        Assert.Equal(2, info.RowCount);
        Assert.Equal(2, info.Columns.Count);
        Assert.All(info.Columns, column => Assert.Null(column.InferredHeader));
        Assert.Throws<InvalidOperationException>(() => _ = info.InferredHeaderRowIndex);
    }

    [Fact]
    public void AnalyzeSheet_AfterDispose_ThrowsObjectDisposedException()
    {
        using var ms = BuildWorkbook(WorkbookXmlOneSheet, RelsXmlOneSheet, EmptySheetXml);
        var workbook = ExcelWorkbook.Open(ms);
        workbook.Dispose();

        Assert.Throws<ObjectDisposedException>(() => workbook.AnalyzeSheet("Data"));
    }

    [Fact]
    public void Analyze_ComplexWorkbook_SurfacesChartsAndSheetArtifacts()
    {
        using var workbook = ExcelWorkbook.Open(TestFilePath("complex_workbook.xlsx"));

        WorkbookInfo info = workbook.Analyze();

        Assert.Equal(4, info.Sheets.Count);
        Assert.Equal(3, info.Charts.Count);
        Assert.Single(info.PivotTables);

        SheetInfo calculator = Assert.Single(
            info.Sheets,
            s => string.Equals(s.SheetName, "Calculator", StringComparison.Ordinal));
        Assert.Equal(2, calculator.Exact.ConditionalFormattingCount);
        Assert.Contains(
            calculator.Observed.FormulaColumns,
            c => string.Equals(c.ColumnLabel, "J", StringComparison.Ordinal) && c.FormulaCount == 42);
        Assert.NotEmpty(calculator.Inferred.Regions);

        SheetInfo charts = Assert.Single(
            info.Sheets,
            s => string.Equals(s.SheetName, "Charts", StringComparison.Ordinal));
        Assert.Equal(1, charts.Exact.DrawingCount);
        Assert.Equal(3, charts.Exact.Charts.Count);
        Assert.Contains(charts.Exact.Charts, c => c.PartPath.EndsWith("chart1.xml", StringComparison.Ordinal));
        Assert.Contains(charts.Inferred.Regions, region => region.ColumnCount >= 3);

        SheetInfo pivotSheet = Assert.Single(
            info.Sheets,
            s => string.Equals(s.SheetName, "Pivot-Analysis", StringComparison.Ordinal));
        Assert.Single(pivotSheet.Exact.PivotTables);
        Assert.Equal("Pivot-Analysis", pivotSheet.Exact.PivotTables[0].Sheet);
    }

    [Fact]
    public void Analyze_ComplexWorkbook_SeparatesValueAndDeclaredRanges()
    {
        using var workbook = ExcelWorkbook.Open(TestFilePath("complex_workbook.xlsx"));

        SheetInfo info = workbook.AnalyzeSheet("Calculator");

        Assert.NotNull(info.Exact.DeclaredDimension);
        Assert.NotNull(info.Observed.ValueUsedRange);
        Assert.NotEqual(info.Exact.DeclaredDimension, info.Observed.ValueUsedRange);
        Assert.Contains(
            info.Inferred.Warnings,
            warning => string.Equals(warning.Code, "declared-dimension-mismatch", StringComparison.Ordinal));
    }

    [Fact]
    public void Analyze_ComplexWorkbook_ExactLevel_SkipsObservedAndInference()
    {
        using var workbook = ExcelWorkbook.Open(TestFilePath("complex_workbook.xlsx"));

        WorkbookInfo info = workbook.Analyze(AnalysisLevel.Exact);

        Assert.Equal(AnalysisLevel.Exact, info.Level);
        Assert.False(info.HasObserved);
        Assert.False(info.HasInferred);
        Assert.Equal(3, info.Charts.Count);
        Assert.Single(info.PivotTables);

        SheetInfo charts = Assert.Single(
            info.Sheets,
            s => string.Equals(s.SheetName, "Charts", StringComparison.Ordinal));
        Assert.Equal(1, charts.Exact.DrawingCount);
        Assert.Equal(3, charts.Exact.Charts.Count);
        Assert.Throws<InvalidOperationException>(() => _ = charts.UsedRange);

        SheetInfo pivotSheet = Assert.Single(
            info.Sheets,
            s => string.Equals(s.SheetName, "Pivot-Analysis", StringComparison.Ordinal));
        Assert.Single(pivotSheet.Exact.PivotTables);
        Assert.Throws<InvalidOperationException>(() => _ = pivotSheet.RowCount);
    }
}
