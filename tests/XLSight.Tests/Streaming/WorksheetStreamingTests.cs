using System.Globalization;
using System.IO.Compression;
using System.Text;
using Xunit;
using XLSight.Exceptions;
using XLSight.Models;

namespace XLSight.Tests.Streaming;

public sealed class WorksheetStreamingTests
{
    // Workbook: Sheet1 has 2 rows (A1=42, B1="Hello", A2=3.14, B2=true), Sheet2 empty.
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

    private static MemoryStream CreateWorkbookWithLargeSheet(int rowCount)
    {
        var sb = new StringBuilder();
        sb.AppendLine("""<worksheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main"><sheetData>""");
        for (int i = 1; i <= rowCount; i++)
        {
            sb.AppendLine(
                CultureInfo.InvariantCulture,
                $"""    <row r="{i}"><c r="A{i}"><v>{i}</v></c></row>""");
        }

        sb.AppendLine("  </sheetData></worksheet>");

        var ms = new MemoryStream();
        using (var archive = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
        {
            WriteEntry(archive, "xl/workbook.xml", WorkbookXml);
            WriteEntry(archive, "xl/_rels/workbook.xml.rels", RelsXml);
            WriteEntry(archive, "xl/sharedStrings.xml", SstXml);
            WriteEntry(archive, "xl/styles.xml", StylesXml);
            WriteEntry(archive, "xl/worksheets/sheet1.xml", sb.ToString());
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
    public void StreamRange_AllRows_YieldsCorrectRowCount()
    {
        using var ms = CreateWorkbook();
        using var workbook = XLSight.ExcelWorkbook.Open(ms);

        var rows = workbook.StreamRange("Sheet1", "A1:B2").ToList();

        Assert.Equal(2, rows.Count);
    }

    [Fact]
    public void StreamRange_RowValues_DecodedCorrectly()
    {
        using var ms = CreateWorkbook();
        using var workbook = XLSight.ExcelWorkbook.Open(ms);

        var rows = workbook.StreamRange("Sheet1", "A1:B2").Select(r => r.CloneRow()).ToList();

        Assert.Equal(2, rows.Count);

        var row1 = rows[0];
        Assert.Equal(1, row1.RowIndex);
        Assert.Equal(ExcelCellValue.FromNumber(42), row1.GetCell(1));
        Assert.Equal(ExcelCellValue.FromText("Hello"), row1.GetCell(2));

        var row2 = rows[1];
        Assert.Equal(2, row2.RowIndex);
        Assert.Equal(ExcelCellValue.FromNumber(3.14), row2.GetCell(1));
        Assert.Equal(ExcelCellValue.FromBoolean(true), row2.GetCell(2));
    }

    [Fact]
    public void StreamRange_Take10_ReturnsExactly10Rows()
    {
        using var ms = CreateWorkbookWithLargeSheet(20);
        using var workbook = XLSight.ExcelWorkbook.Open(ms);

        var rows = workbook.StreamRange("Sheet1", "A1:A20").Take(10).ToList();

        Assert.Equal(10, rows.Count);
    }

    [Fact]
    public void StreamRange_EmptySheet_YieldsNoRows()
    {
        using var ms = CreateWorkbook();
        using var workbook = XLSight.ExcelWorkbook.Open(ms);

        var rows = workbook.StreamRange("Sheet2", "A1:B10").ToList();

        Assert.Empty(rows);
    }

    [Fact]
    public void StreamRange_AfterDispose_ThrowsObjectDisposedException()
    {
        using var ms = CreateWorkbook();
        var workbook = XLSight.ExcelWorkbook.Open(ms);
        workbook.Dispose();

        Assert.Throws<ObjectDisposedException>(() => workbook.StreamRange("Sheet1", "A1:B2").ToList());
    }

    [Fact]
    public async Task StreamRangeAsync_AllRows_YieldsCorrectRowCount()
    {
        using var ms = CreateWorkbook();
        using var workbook = XLSight.ExcelWorkbook.Open(ms);

        var rows = new List<ExcelRow>();
        await foreach (var row in workbook.StreamRangeAsync("Sheet1", "A1:B2", ct: TestContext.Current.CancellationToken))
        {
            rows.Add(row);
        }

        Assert.Equal(2, rows.Count);
    }

    [Fact]
    public void StreamSheet_AllRows_YieldsAllRows()
    {
        using var ms = CreateWorkbook();
        using var workbook = XLSight.ExcelWorkbook.Open(ms);

        var rows = workbook.StreamSheet("Sheet1").ToList();

        Assert.Equal(2, rows.Count);
        Assert.Equal(1, rows[0].RowIndex);
        Assert.Equal(2, rows[1].RowIndex);
    }
}
