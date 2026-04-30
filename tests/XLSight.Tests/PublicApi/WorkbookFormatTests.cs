using System.IO.Compression;
using System.Text;
using Xunit;

namespace XLSight.Tests.PublicApi;

public sealed class WorkbookFormatTests
{
    private const string WorkbookXml = """
        <workbook xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main"
                  xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships">
          <sheets>
            <sheet name="Sheet1" sheetId="1" r:id="rId1" />
          </sheets>
        </workbook>
        """;

    private const string RelsXml = """
        <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
          <Relationship Id="rId1" Target="worksheets/sheet1.xml" />
        </Relationships>
        """;

    private const string SheetXml = """
        <worksheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main">
          <sheetData />
        </worksheet>
        """;

    [Theory]
    [InlineData(".xlsx", WorkbookFormat.Xlsx)]
    [InlineData(".xlsm", WorkbookFormat.Xlsm)]
    public void Open_FromPath_UsesExtensionFormat(string extension, WorkbookFormat expectedFormat)
    {
        string path = CreateTempWorkbook(extension);
        try
        {
            using var workbook = ExcelWorkbook.Open(path);

            Assert.Equal(expectedFormat, workbook.Format);
            Assert.False(workbook.HasMacros);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Open_FromPath_DetectsOpenXmlMacros()
    {
        string path = CreateTempWorkbook(".xlsm", hasMacros: true);
        try
        {
            using var workbook = ExcelWorkbook.Open(path);

            Assert.Equal(WorkbookFormat.Xlsm, workbook.Format);
            Assert.True(workbook.HasMacros);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Open_FromStream_DoesNotInferXlsmFormat()
    {
        using var stream = CreateWorkbookStream(hasMacros: true);
        using var workbook = ExcelWorkbook.Open(stream);

        Assert.Equal(WorkbookFormat.Xlsx, workbook.Format);
        Assert.True(workbook.HasMacros);
    }

    [Fact]
    public void GetVbaProject_WithoutMacros_ReturnsNull()
    {
        using var stream = CreateWorkbookStream();
        using var workbook = ExcelWorkbook.Open(stream);

        Assert.Null(workbook.GetVbaProject());
    }

    [Fact]
    public void GetVbaProject_WithMalformedProject_ThrowsInvalidData()
    {
        using var stream = CreateWorkbookStream(hasMacros: true);
        using var workbook = ExcelWorkbook.Open(stream);

        Assert.Throws<InvalidDataException>(() => workbook.GetVbaProject());
    }

    [Fact]
    public void Analyze_WithMalformedVbaProject_AddsWorkbookWarning()
    {
        using var stream = CreateWorkbookStream(hasMacros: true);
        using var workbook = ExcelWorkbook.Open(stream);

        var info = workbook.Analyze();

        Assert.True(info.HasMacros);
        Assert.Null(info.Exact.VbaProject);
        Assert.Contains(info.Exact.Warnings, warning => string.Equals(warning.Code, "vba.parse.failed", StringComparison.Ordinal));
    }

    [Fact]
    public void Open_FromXlsbPath_ReadsRealWorkbook()
    {
        using var workbook = ExcelWorkbook.Open(GetTestDataPath("complex_workbook.xlsb"));

        Assert.Equal(WorkbookFormat.Xlsb, workbook.Format);
        Assert.False(workbook.HasMacros);
        Assert.Contains(
            workbook.SheetNames,
            sheetName => string.Equals(sheetName, "Scenarios", StringComparison.Ordinal));

        var rows = workbook.StreamSheet("Scenarios").Take(5).ToArray();
        Assert.Contains(rows, XLSightTestHelpers.RowHasValue);
    }

    [Fact]
    public async Task OpenAsync_FromXlsbPath_ReadsRealWorkbook()
    {
        using var workbook = await ExcelWorkbook.OpenAsync(
            GetTestDataPath("complex_workbook.xlsb"),
            TestContext.Current.CancellationToken);

        Assert.Equal(WorkbookFormat.Xlsb, workbook.Format);
        Assert.False(workbook.HasMacros);
        Assert.Contains(
            workbook.SheetNames,
            sheetName => string.Equals(sheetName, "Scenarios", StringComparison.Ordinal));
    }

    [Fact]
    public void GetVbaProject_FromXlsbWithoutMacros_ReturnsNull()
    {
        using var workbook = ExcelWorkbook.Open(GetTestDataPath("complex_workbook.xlsb"));

        Assert.Null(workbook.GetVbaProject());
    }

    private static string CreateTempWorkbook(string extension, bool hasMacros = false)
    {
        string path = Path.Combine(Path.GetTempPath(), $"XLSight_{Guid.NewGuid():N}{extension}");
        using var stream = CreateWorkbookStream(hasMacros);
        using var file = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        stream.CopyTo(file);
        return path;
    }

    private static string GetTestDataPath(string fileName) =>
        Path.Combine(AppContext.BaseDirectory, "TestData", fileName);

    private static MemoryStream CreateWorkbookStream(bool hasMacros = false)
    {
        var stream = new MemoryStream();
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            WriteEntry(archive, "xl/workbook.xml", WorkbookXml);
            WriteEntry(archive, "xl/_rels/workbook.xml.rels", RelsXml);
            WriteEntry(archive, "xl/worksheets/sheet1.xml", SheetXml);
            if (hasMacros)
            {
                WriteEntry(archive, "xl/vbaProject.bin", string.Empty);
            }
        }

        stream.Position = 0;
        return stream;
    }

    private static void WriteEntry(ZipArchive archive, string path, string content)
    {
        ZipArchiveEntry entry = archive.CreateEntry(path);
        using Stream entryStream = entry.Open();
        using var writer = new StreamWriter(entryStream, Encoding.UTF8, leaveOpen: true);
        writer.Write(content);
    }
}
