using System.IO.Compression;
using System.Text;
using XLSight.Internal.Analysis;
using XLSight.Internal.Packaging;
using Xunit;

namespace XLSight.Tests.Analysis;

/// <summary>
/// Tests for <see cref="AnalyzerMetadataReader"/> covering the serial Read path
/// and the ReadParallel fallback for single-sheet workbooks.
/// </summary>
public sealed class AnalyzerMetadataReaderTests : IDisposable
{
    private readonly string _tempFile;

    public AnalyzerMetadataReaderTests()
    {
        _tempFile = Path.Combine(AppContext.BaseDirectory, $"AMR_Temp_{Guid.NewGuid():N}.xlsx");
    }

    public void Dispose()
    {
        if (File.Exists(_tempFile)) { File.Delete(_tempFile); }
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static void WriteEntry(ZipArchive archive, string path, string content)
    {
        var entry = archive.CreateEntry(path);
        using var stream = entry.Open();
        using var writer = new StreamWriter(stream, Encoding.UTF8, leaveOpen: true);
        writer.Write(content);
    }

    private const string StylesXml = """
        <styleSheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main">
          <cellXfs><xf numFmtId="0" /></cellXfs>
        </styleSheet>
        """;

    private const string EmptySheetXml = """
        <worksheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main">
          <sheetData />
        </worksheet>
        """;

    private static MemoryStream BuildTwoSheetPackage()
    {
        var ms = new MemoryStream();
        using (var archive = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
        {
            WriteEntry(archive, "xl/workbook.xml", """
                <workbook xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main"
                          xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships">
                  <sheets>
                    <sheet name="Sheet1" sheetId="1" r:id="rId1" />
                    <sheet name="Sheet2" sheetId="2" r:id="rId2" />
                  </sheets>
                </workbook>
                """);
            WriteEntry(archive, "xl/_rels/workbook.xml.rels", """
                <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
                  <Relationship Id="rId1" Target="worksheets/sheet1.xml" />
                  <Relationship Id="rId2" Target="worksheets/sheet2.xml" />
                </Relationships>
                """);
            WriteEntry(archive, "xl/styles.xml", StylesXml);
            WriteEntry(archive, "xl/worksheets/sheet1.xml", EmptySheetXml);
            WriteEntry(archive, "xl/worksheets/sheet2.xml", EmptySheetXml);
        }
        ms.Position = 0;
        return ms;
    }

    private static WorkbookMetadata ParseMetadata(XlsxPackage package)
    {
        using Stream wbStream = package.GetEntry("xl/workbook.xml")!.Open();
        using Stream relsStream = package.GetEntry("xl/_rels/workbook.xml.rels")!.Open();
        WorkbookParser.ParsedWorkbookDefinition def = WorkbookParser.Parse(wbStream);
        return RelationshipsParser.Parse(relsStream, def);
    }

    // ── AnalyzerMetadataReader.Read (serial path) ─────────────────────────────

    [Fact]
    public void Read_TwoSheetWorkbook_ReturnsBothSheets()
    {
        using var ms = BuildTwoSheetPackage();
        using var package = XlsxPackage.Open(ms);
        WorkbookMetadata metadata = ParseMetadata(package);

        AnalyzerMetadata result = AnalyzerMetadataReader.Read(package, metadata);

        Assert.Equal(2, result.SheetsByPath.Count);
        Assert.NotNull(result.WorkbookExact);
    }

    [Fact]
    public void Read_EmptyWorkbook_NoSheets_ReturnsEmptyResult()
    {
        var ms = new MemoryStream();
        using (var archive = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
        {
            WriteEntry(archive, "xl/workbook.xml", """
                <workbook xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main"
                          xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships">
                  <sheets />
                </workbook>
                """);
            WriteEntry(archive, "xl/_rels/workbook.xml.rels", """
                <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
                </Relationships>
                """);
        }
        ms.Position = 0;
        using var package = XlsxPackage.Open(ms);
        using Stream wbStream = package.GetEntry("xl/workbook.xml")!.Open();
        using Stream relsStream = package.GetEntry("xl/_rels/workbook.xml.rels")!.Open();
        WorkbookParser.ParsedWorkbookDefinition def = WorkbookParser.Parse(wbStream);
        WorkbookMetadata metadata = RelationshipsParser.Parse(relsStream, def);

        AnalyzerMetadata result = AnalyzerMetadataReader.Read(package, metadata);

        Assert.Empty(result.SheetsByPath);
        Assert.Empty(result.WorkbookExact.Tables);
    }

    // ── AnalyzerMetadataReader.ReadParallel ───────────────────────────────────

    [Fact]
    public void ReadParallel_SingleSheetWorkbook_DelegatesToRead()
    {
        // Sheets.Count <= 1 → ReadParallel falls back to Read
        var ms = new MemoryStream();
        using (var archive = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
        {
            WriteEntry(archive, "xl/workbook.xml", """
                <workbook xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main"
                          xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships">
                  <sheets>
                    <sheet name="Only" sheetId="1" r:id="rId1" />
                  </sheets>
                </workbook>
                """);
            WriteEntry(archive, "xl/_rels/workbook.xml.rels", """
                <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
                  <Relationship Id="rId1" Target="worksheets/sheet1.xml" />
                </Relationships>
                """);
            WriteEntry(archive, "xl/worksheets/sheet1.xml", EmptySheetXml);
        }
        ms.Position = 0;
        using var package = XlsxPackage.Open(ms);
        using Stream wbStream = package.GetEntry("xl/workbook.xml")!.Open();
        using Stream relsStream = package.GetEntry("xl/_rels/workbook.xml.rels")!.Open();
        WorkbookParser.ParsedWorkbookDefinition def = WorkbookParser.Parse(wbStream);
        WorkbookMetadata metadata = RelationshipsParser.Parse(relsStream, def);

        AnalyzerMetadata result = AnalyzerMetadataReader.ReadParallel(package, metadata);

        Assert.Single(result.SheetsByPath);
    }

    [Fact]
    public void ReadParallel_TwoSheetWorkbook_ReturnsAllSheets()
    {
        using var ms = BuildTwoSheetPackage();
        using var package = XlsxPackage.Open(ms);
        WorkbookMetadata metadata = ParseMetadata(package);

        // Force ReadParallel (even though not file-backed it still runs serial internally)
        AnalyzerMetadata result = AnalyzerMetadataReader.ReadParallel(package, metadata);

        Assert.Equal(2, result.SheetsByPath.Count);
        Assert.Empty(result.WorkbookExact.Tables);
        Assert.Empty(result.WorkbookExact.PivotTables);
        Assert.Empty(result.WorkbookExact.Charts);
    }

    // ── File-backed package (ReadParallel via ExcelWorkbook) ──────────────────

    [Fact]
    public void ExcelWorkbook_Open_FilePath_AnalyzesTwoSheets()
    {
        // Write a two-sheet workbook to disk so IsFileBacked = true → ReadParallel runs
        using (var ms = BuildTwoSheetPackage())
        using (var fs = File.Create(_tempFile))
        {
            ms.CopyTo(fs);
        }

        using var workbook = XLSight.ExcelWorkbook.Open(_tempFile);
        XLSight.Models.Analysis.WorkbookInfo info = workbook.Analyze();

        Assert.Equal(2, info.Sheets.Count);
        Assert.Equal("Sheet1", info.Sheets[0].SheetName);
        Assert.Equal("Sheet2", info.Sheets[1].SheetName);
    }



    // ── Workbook with a structured table ─────────────────────────────────────

    private static MemoryStream BuildPackageWithTable()
    {
        const string tableRel = "http://schemas.openxmlformats.org/officeDocument/2006/relationships/table";
        var ms = new MemoryStream();
        using (var archive = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
        {
            WriteEntry(archive, "xl/workbook.xml", """
                <workbook xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main"
                          xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships">
                  <sheets>
                    <sheet name="Data" sheetId="1" r:id="rId1" />
                  </sheets>
                </workbook>
                """);
            WriteEntry(archive, "xl/_rels/workbook.xml.rels", """
                <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
                  <Relationship Id="rId1" Target="worksheets/sheet1.xml" />
                </Relationships>
                """);
            WriteEntry(archive, "xl/styles.xml", StylesXml);
            WriteEntry(archive, "xl/worksheets/sheet1.xml", EmptySheetXml);
            // Sheet relationships: points to table
            WriteEntry(archive, "xl/worksheets/_rels/sheet1.xml.rels", $"""
                <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
                  <Relationship Id="rId1" Type="{tableRel}" Target="../tables/table1.xml" />
                </Relationships>
                """);
            // Table definition
            WriteEntry(archive, "xl/tables/table1.xml", """
                <table xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main"
                       id="1" name="SalesTable" displayName="SalesTable" ref="A1:C4">
                  <tableColumns count="3">
                    <tableColumn id="1" name="Product" />
                    <tableColumn id="2" name="Amount" />
                    <tableColumn id="3" name="Date" />
                  </tableColumns>
                </table>
                """);
        }
        ms.Position = 0;
        return ms;
    }

    [Fact]
    public void Read_WorkbookWithTable_ExtractsTableInfo()
    {
        using var ms = BuildPackageWithTable();
        using var package = XlsxPackage.Open(ms);
        using Stream wbStream = package.GetEntry("xl/workbook.xml")!.Open();
        using Stream relsStream = package.GetEntry("xl/_rels/workbook.xml.rels")!.Open();
        WorkbookParser.ParsedWorkbookDefinition def = WorkbookParser.Parse(wbStream);
        WorkbookMetadata metadata = RelationshipsParser.Parse(relsStream, def);

        AnalyzerMetadata result = AnalyzerMetadataReader.Read(package, metadata);

        Assert.Single(result.WorkbookExact.Tables);
        XLSight.Models.Analysis.TableInfo table = result.WorkbookExact.Tables[0];
        Assert.Equal("SalesTable", table.Name);
        Assert.Equal("Data", table.Sheet);
        Assert.Equal(3, table.ColumnNames.Count);
        Assert.Equal("Product", table.ColumnNames[0]);
        Assert.Equal("Amount", table.ColumnNames[1]);
    }


    [Fact]
    public void Read_WorkbookWithTable_AndLargeRef_ParsesRangeViaStringPath()
    {
        const string tableRel = "http://schemas.openxmlformats.org/officeDocument/2006/relationships/table";
        string hugeRef = "A1:" + new string('A', 80);

        var ms = new MemoryStream();
        using (var archive = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
        {
            WriteEntry(archive, "xl/workbook.xml", """
                <workbook xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main"
                          xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships">
                  <sheets>
                    <sheet name="Data" sheetId="1" r:id="rId1" />
                  </sheets>
                </workbook>
                """);
            WriteEntry(archive, "xl/_rels/workbook.xml.rels", """
                <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
                  <Relationship Id="rId1" Target="worksheets/sheet1.xml" />
                </Relationships>
                """);
            WriteEntry(archive, "xl/worksheets/sheet1.xml", EmptySheetXml);
            WriteEntry(archive, "xl/worksheets/_rels/sheet1.xml.rels", $"""
                <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
                  <Relationship Id="rId1" Type="{tableRel}" Target="../tables/table1.xml" />
                </Relationships>
                """);
            WriteEntry(archive, "xl/tables/table1.xml", $"""
                <table xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main"
                       id="1" name="HugeRef" displayName="HugeRef" ref="{hugeRef}">
                  <tableColumns count="1"><tableColumn id="1" name="Only" /></tableColumns>
                </table>
                """);
        }
        ms.Position = 0;

        using var package = XlsxPackage.Open(ms);
        WorkbookMetadata metadata = ParseMetadata(package);
        AnalyzerMetadata result = AnalyzerMetadataReader.Read(package, metadata);

        Assert.Empty(result.WorkbookExact.Tables);
    }


    // ── Workbook with comments ────────────────────────────────────────────────

    private static MemoryStream BuildPackageWithComments()
    {
        const string commentsRel = "http://schemas.openxmlformats.org/officeDocument/2006/relationships/comments";
        var ms = new MemoryStream();
        using (var archive = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
        {
            WriteEntry(archive, "xl/workbook.xml", """
                <workbook xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main"
                          xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships">
                  <sheets>
                    <sheet name="Sheet1" sheetId="1" r:id="rId1" />
                  </sheets>
                </workbook>
                """);
            WriteEntry(archive, "xl/_rels/workbook.xml.rels", """
                <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
                  <Relationship Id="rId1" Target="worksheets/sheet1.xml" />
                </Relationships>
                """);
            WriteEntry(archive, "xl/styles.xml", StylesXml);
            WriteEntry(archive, "xl/worksheets/sheet1.xml", EmptySheetXml);
            WriteEntry(archive, "xl/worksheets/_rels/sheet1.xml.rels", $"""
                <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
                  <Relationship Id="rId1" Type="{commentsRel}" Target="../comments1.xml" />
                </Relationships>
                """);
            // Comments file with 3 <comment> elements
            WriteEntry(archive, "xl/comments1.xml", """
                <comments xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main">
                  <commentList>
                    <comment ref="A1" authorId="0"><text><t>Note1</t></text></comment>
                    <comment ref="B2" authorId="0"><text><t>Note2</t></text></comment>
                    <comment ref="C3" authorId="0"><text><t>Note3</t></text></comment>
                  </commentList>
                </comments>
                """);
        }
        ms.Position = 0;
        return ms;
    }

    [Fact]
    public void Read_WorkbookWithComments_CountsComments()
    {
        using var ms = BuildPackageWithComments();
        using var package = XlsxPackage.Open(ms);
        using Stream wbStream = package.GetEntry("xl/workbook.xml")!.Open();
        using Stream relsStream = package.GetEntry("xl/_rels/workbook.xml.rels")!.Open();
        WorkbookParser.ParsedWorkbookDefinition def = WorkbookParser.Parse(wbStream);
        WorkbookMetadata metadata = RelationshipsParser.Parse(relsStream, def);

        AnalyzerMetadata result = AnalyzerMetadataReader.Read(package, metadata);

        Assert.Single(result.SheetsByPath);
        var sheetMeta = result.SheetsByPath.Values.Single();
        Assert.Equal(3, sheetMeta.Exact.CommentCount);
    }
}
