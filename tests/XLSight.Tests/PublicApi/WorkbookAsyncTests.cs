using System.IO.Compression;
using System.Text;
using XLSight.Models;
using XLSight.Models.Analysis;
using Xunit;

namespace XLSight.Tests.PublicApi;

public sealed class WorkbookAsyncTests
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

    private static void WriteWorkbookToFile(string filePath)
    {
        using var ms = CreateWorkbook();
        using var fs = new FileStream(filePath, FileMode.Create, FileAccess.Write, FileShare.None);
        ms.CopyTo(fs);
    }

    [Fact]
    public async Task OpenAsync_FromStream_Succeeds()
    {
        using var ms = CreateWorkbook();
        await using var workbook = await XLSight.ExcelWorkbook.OpenAsync(ms, TestContext.Current.CancellationToken);

        Assert.Equal(2, workbook.SheetNames.Count);
        Assert.Equal("Sheet1", workbook.SheetNames[0]);
        Assert.Equal("Sheet2", workbook.SheetNames[1]);
    }

    [Fact]
    public async Task OpenAsync_FromFilePath_Succeeds()
    {
        string tempFile = Path.GetTempFileName();
        try
        {
            WriteWorkbookToFile(tempFile);

            await using var workbook = await XLSight.ExcelWorkbook.OpenAsync(tempFile, TestContext.Current.CancellationToken);

            Assert.Equal(2, workbook.SheetNames.Count);
            Assert.Equal("Sheet1", workbook.SheetNames[0]);
            Assert.Equal("Sheet2", workbook.SheetNames[1]);
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    [Fact]
    public async Task OpenAsync_FromFilePath_ReadRangeAsync_ReturnsCorrectValues()
    {
        // Ensures file stream stays alive for the workbook lifetime (not disposed by 'await using' on open).
        string tempFile = Path.GetTempFileName();
        try
        {
            WriteWorkbookToFile(tempFile);

            await using var workbook = await XLSight.ExcelWorkbook.OpenAsync(tempFile, TestContext.Current.CancellationToken);
            var result = await workbook.ReadRangeAsync("Sheet1", "A1:B2", ct: TestContext.Current.CancellationToken);

            Assert.Equal(2, result.Width);
            Assert.Equal(2, result.Height);
            Assert.Equal(ExcelCellValue.FromNumber(42), result[0, 0]); // A1
            Assert.Equal(ExcelCellValue.FromText("Hello"), result[0, 1]); // B1
        }
        finally
        {
            File.Delete(tempFile);
        }
    }
    [Fact]
    public async Task ReadRangeAsync_KnownValues_DecodesCorrectly()
    {
        using var ms = CreateWorkbook();
        await using var workbook = await XLSight.ExcelWorkbook.OpenAsync(ms, TestContext.Current.CancellationToken);

        var result = await workbook.ReadRangeAsync("Sheet1", "A1:B2", ct: TestContext.Current.CancellationToken);

        Assert.Equal(2, result.Width);
        Assert.Equal(2, result.Height);
        Assert.Equal(ExcelCellValue.FromNumber(42), result[0, 0]); // A1
        Assert.Equal(ExcelCellValue.FromText("Hello"), result[0, 1]); // B1
        Assert.Equal(ExcelCellValue.FromNumber(3.14), result[1, 0]); // A2
        Assert.Equal(ExcelCellValue.FromBoolean(true), result[1, 1]); // B2
    }

    [Fact]
    public async Task ReadCellAsync_KnownValue_DecodesCorrectly()
    {
        using var ms = CreateWorkbook();
        await using var workbook = await XLSight.ExcelWorkbook.OpenAsync(ms, TestContext.Current.CancellationToken);

        var result = await workbook.ReadCellAsync("Sheet1", "A1", ct: TestContext.Current.CancellationToken);

        Assert.Equal(ExcelCellValue.FromNumber(42), result.Value);
        Assert.Equal(1, result.Row);
        Assert.Equal(1, result.Column);
    }

    [Fact]
    public async Task AnalyzeSheetAsync_WithData_ReturnsCorrectSheetInfo()
    {
        using var ms = CreateWorkbook();
        await using var workbook = await XLSight.ExcelWorkbook.OpenAsync(ms, TestContext.Current.CancellationToken);

        var info = await workbook.AnalyzeSheetAsync("Sheet1", ct: TestContext.Current.CancellationToken);

        Assert.Equal("Sheet1", info.SheetName);
        Assert.False(info.IsEmpty);
        Assert.Equal(2, info.RowCount);
        Assert.Equal(2, info.ColumnCount);
    }

    [Fact]
    public async Task AnalyzeAsync_MultipleSheets_ReturnsAllSheets()
    {
        using var ms = CreateWorkbook();
        await using var workbook = await XLSight.ExcelWorkbook.OpenAsync(ms, TestContext.Current.CancellationToken);

        var info = await workbook.AnalyzeAsync(TestContext.Current.CancellationToken);

        Assert.Equal(2, info.Sheets.Count);
        Assert.False(info.HasMacros);
        Assert.False(info.IsDate1904);
    }

    [Fact]
    public async Task AnalyzeSheetAsync_ExactLevel_ReturnsExactMetadataOnly()
    {
        using var ms = CreateWorkbook();
        await using var workbook = await XLSight.ExcelWorkbook.OpenAsync(ms, TestContext.Current.CancellationToken);

        var info = await workbook.AnalyzeSheetAsync("Sheet1", AnalysisLevel.Exact, TestContext.Current.CancellationToken);

        Assert.Equal(AnalysisLevel.Exact, info.Level);
        Assert.False(info.HasObserved);
        Assert.False(info.HasInferred);
        Assert.NotNull(info.Exact.DeclaredDimension);
        Assert.Throws<InvalidOperationException>(() => _ = info.RowCount);
    }

    [Fact]
    public async Task ReadRangeAsync_WithPreCancelledToken_ThrowsOperationCanceledException()
    {
        using var ms = CreateWorkbook();
        await using var workbook = await XLSight.ExcelWorkbook.OpenAsync(ms, TestContext.Current.CancellationToken);
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAsync<OperationCanceledException>(
            () => workbook.ReadRangeAsync("Sheet1", "A1:B2", ct: cts.Token));
    }

    [Fact]
    public async Task AnalyzeSheetAsync_WithPreCancelledToken_ThrowsOperationCanceledException()
    {
        using var ms = CreateWorkbook();
        await using var workbook = await XLSight.ExcelWorkbook.OpenAsync(ms, TestContext.Current.CancellationToken);
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAsync<OperationCanceledException>(
            () => workbook.AnalyzeSheetAsync("Sheet1", cts.Token));
    }

    [Fact]
    public async Task ReadRangeAsync_AfterDispose_ThrowsObjectDisposedException()
    {
        using var ms = CreateWorkbook();
        var workbook = await XLSight.ExcelWorkbook.OpenAsync(ms, TestContext.Current.CancellationToken);
        await workbook.DisposeAsync();

        await Assert.ThrowsAsync<ObjectDisposedException>(
            () => workbook.ReadRangeAsync("Sheet1", "A1:B2", ct: TestContext.Current.CancellationToken));
    }
}
