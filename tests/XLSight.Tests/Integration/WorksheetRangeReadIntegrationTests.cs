using System.IO.Compression;
using System.Text;
using Xunit;
using XLSight.Internal.Readers.Xlsx;
using XLSight.Models;
using XLSight.Internal.Packaging;
using XLSight.Internal.Metadata;
using XLSight.Internal.Sinks;

namespace XLSight.Tests.Integration;

/// <summary>
/// End-to-end: open xlsx → parse metadata → load SST + styles → scan worksheet
/// → decode cells → verify values.
/// </summary>
public sealed class WorksheetRangeReadIntegrationTests
{
    // Minimal workbook:
    //   Sheet1: A1=42 (number), B1="Hello" (shared string), A2=3.14 (number), B2=true (boolean)
    //   Sheet2: empty

    private const string WorkbookXml = """
        <workbook xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main"
                  xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships">
          <sheets>
            <sheet name="Sheet1" sheetId="1" r:id="rId1" />
            <sheet name="Sheet2" sheetId="2" r:id="rId2" />
          </sheets>
        </workbook>
        """;

    private const string RelsXml = """
        <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
          <Relationship Id="rId1" Target="worksheets/sheet1.xml" />
          <Relationship Id="rId2" Target="worksheets/sheet2.xml" />
        </Relationships>
        """;

    private const string SstXml = """
        <sst xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main" uniqueCount="1">
          <si><t>Hello</t></si>
        </sst>
        """;

    private const string StylesXml = """
        <styleSheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main">
          <cellXfs>
            <xf numFmtId="0" />
          </cellXfs>
        </styleSheet>
        """;

    private const string Sheet1Xml = """
        <worksheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main">
          <dimension ref="A1:B2" />
          <sheetData>
            <row r="1">
              <c r="A1"><v>42</v></c>
              <c r="B1" t="s"><v>0</v></c>
            </row>
            <row r="2">
              <c r="A2"><v>3.14</v></c>
              <c r="B2" t="b"><v>1</v></c>
            </row>
          </sheetData>
        </worksheet>
        """;

    private const string Sheet2Xml = """
        <worksheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main">
          <sheetData />
        </worksheet>
        """;

    private static MemoryStream CreateWorkbook()
    {
        var ms = new MemoryStream();
        using (var archive = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
        {
            WriteEntry(archive, "xl/workbook.xml", WorkbookXml);
            WriteEntry(archive, "xl/_rels/workbook.xml.rels", RelsXml);
            WriteEntry(archive, "xl/sharedStrings.xml", SstXml);
            WriteEntry(archive, "xl/styles.xml", StylesXml);
            WriteEntry(archive, "xl/worksheets/sheet1.xml", Sheet1Xml);
            WriteEntry(archive, "xl/worksheets/sheet2.xml", Sheet2Xml);
        }

        ms.Position = 0;
        return ms;
    }

    private static void WriteEntry(ZipArchive archive, string path, string content)
    {
        ZipArchiveEntry entry = archive.CreateEntry(path);
        using Stream entryStream = entry.Open();
        using var writer = new StreamWriter(entryStream, Encoding.UTF8, leaveOpen: true);
        writer.Write(content);
    }

    [Fact]
    public void OpenPackage_ParseMetadata_TwoSheetsWithCorrectPaths()
    {
        using var ms = CreateWorkbook();
        using XlsxPackage package = XlsxPackage.Open(ms);

        using Stream workbookStream = package.GetEntry("xl/workbook.xml")!.Open();
        using Stream relsStream = package.GetEntry("xl/_rels/workbook.xml.rels")!.Open();

        WorkbookParser.ParsedWorkbookDefinition def = WorkbookParser.Parse(workbookStream);
        WorkbookMetadata metadata = RelationshipsParser.Parse(relsStream, def);

        Assert.Equal(2, metadata.Sheets.Count);
        Assert.Equal("Sheet1", metadata.Sheets[0].Name);
        Assert.Equal("xl/worksheets/sheet1.xml", metadata.Sheets[0].Path);
        Assert.Equal("Sheet2", metadata.Sheets[1].Name);
        Assert.Equal("xl/worksheets/sheet2.xml", metadata.Sheets[1].Path);
    }

    [Fact]
    public void ReadRange_AllCellTypes_EndToEnd()
    {
        using var ms = CreateWorkbook();
        using XlsxPackage package = XlsxPackage.Open(ms);

        // Parse metadata to locate the worksheet
        using Stream workbookStream = package.GetEntry("xl/workbook.xml")!.Open();
        using Stream relsStream = package.GetEntry("xl/_rels/workbook.xml.rels")!.Open();
        WorkbookParser.ParsedWorkbookDefinition def = WorkbookParser.Parse(workbookStream);
        WorkbookMetadata metadata = RelationshipsParser.Parse(relsStream, def);

        // Load SST and styles
        using Stream sstStream = package.GetEntry("xl/sharedStrings.xml")!.Open();
        SharedStringTable sharedStrings = SharedStringsParser.Parse(sstStream);

        using Stream stylesStream = package.GetEntry("xl/styles.xml")!.Open();
        StyleTable styles = StylesParser.Parse(stylesStream);

        // Scan A1:B2 of Sheet1
        var range = new ExcelRange(new ExcelAddress(1, 1), new ExcelAddress(2, 2));
        var buffer = new ExcelCellValue[range.Width * range.Height];
        var sink = new RangeSink(range, buffer);

        using Stream sheetStream = package.GetEntry(metadata.Sheets[0].Path)!.Open();
        XlsxSheetScanner.ScanSheet(sheetStream, sharedStrings, styles, isDate1904: false, range, ref sink);

        Assert.Equal(ExcelCellValue.FromNumber(42),    buffer[0]); // A1
        Assert.Equal(ExcelCellValue.FromText("Hello"), buffer[1]); // B1
        Assert.Equal(ExcelCellValue.FromNumber(3.14),  buffer[2]); // A2
        Assert.Equal(ExcelCellValue.FromBoolean(true), buffer[3]); // B2
    }

    [Fact]
    public void ReadRange_EmptySheet_BufferStaysEmpty()
    {
        using var ms = CreateWorkbook();
        using XlsxPackage package = XlsxPackage.Open(ms);

        var range = new ExcelRange(new ExcelAddress(1, 1), new ExcelAddress(2, 2));
        var buffer = new ExcelCellValue[range.Width * range.Height];
        var sink = new RangeSink(range, buffer);

        using Stream sheetStream = package.GetEntry("xl/worksheets/sheet2.xml")!.Open();
        XlsxSheetScanner.ScanSheet(sheetStream, SharedStringTable.Empty, StyleTable.Default, isDate1904: false, range, ref sink);

        Assert.All(buffer, cell => Assert.Equal(ExcelCellValue.Empty, cell));
    }
}
