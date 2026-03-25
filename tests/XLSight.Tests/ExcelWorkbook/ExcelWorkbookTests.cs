using System.IO.Compression;
using System.Text;
using Xunit;
using XLSight.Exceptions;
using XLSight.Models;

namespace XLSight.Tests.ExcelWorkbook;

public sealed class ExcelWorkbookTests
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
    public void Open_FromStream_Succeeds()
    {
        using var ms = CreateWorkbook();
        using var workbook = XLSight.ExcelWorkbook.Open(ms);

        Assert.Equal(2, workbook.SheetNames.Count);
        Assert.Equal("Sheet1", workbook.SheetNames[0]);
        Assert.Equal("Sheet2", workbook.SheetNames[1]);
    }

    [Fact]
    public void Open_NonSeekableStream_ThrowsInvalidOperationException()
    {
        using var ms = CreateWorkbook();
        using var nonSeekable = new NonSeekableStream(ms);

        Assert.Throws<InvalidOperationException>(() => XLSight.ExcelWorkbook.Open(nonSeekable));
    }

    [Fact]
    public void Open_FromFilePath_Succeeds()
    {
        string tempFile = Path.GetTempFileName();
        try
        {
            WriteWorkbookToFile(tempFile);

            using var workbook = XLSight.ExcelWorkbook.Open(tempFile);

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
    public void Open_FromFilePath_ReadRange_ReturnsCorrectValues()
    {
        // Ensures file stream stays alive for the workbook lifetime (not disposed by 'using' on open).
        string tempFile = Path.GetTempFileName();
        try
        {
            WriteWorkbookToFile(tempFile);

            using var workbook = XLSight.ExcelWorkbook.Open(tempFile);
            var result = workbook.ReadRange("Sheet1", "A1:B2");

            Assert.Equal(2, result.Width);
            Assert.Equal(2, result.Height);
            Assert.Equal(ExcelCellValue.FromNumber(42),    result[0, 0]); // A1
            Assert.Equal(ExcelCellValue.FromText("Hello"), result[0, 1]); // B1
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    [Fact]
    public void Open_FromFilePath_StreamSheet_ReturnsRows()
    {
        // Ensures lazy sheet streaming works when opened by file path.
        string tempFile = Path.GetTempFileName();
        try
        {
            WriteWorkbookToFile(tempFile);

            using var workbook = XLSight.ExcelWorkbook.Open(tempFile);
            var rows = workbook.StreamSheet("Sheet1").ToList();

            Assert.Equal(2, rows.Count);
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    [Fact]
    public void Dispose_CallerMustKeepSeekableStreamAlive()
    {
        // For seekable streams, the workbook uses the stream directly (no copy).
        // The caller must keep the stream alive for the workbook's lifetime.
        byte[] bytes = CreateWorkbook().ToArray();
        var callerStream = new MemoryStream(bytes, 0, bytes.Length, writable: false, publiclyVisible: false);
        var workbook = XLSight.ExcelWorkbook.Open(callerStream);

        // Stream is still alive — reads must succeed
        var result = workbook.ReadRange("Sheet1", "A1:B2");
        Assert.Equal(4, result.CellCount);

        workbook.Dispose();
        callerStream.Dispose();
    }

    [Fact]
    public void Dispose_DoubleDispose_DoesNotThrow()
    {
        using var ms = CreateWorkbook();
        var workbook = XLSight.ExcelWorkbook.Open(ms);

        workbook.Dispose();
        workbook.Dispose(); // second call must be a no-op
    }

    [Fact]
    public void ReadRange_KnownValues_DecodesCorrectly()
    {
        using var ms = CreateWorkbook();
        using var workbook = XLSight.ExcelWorkbook.Open(ms);

        var result = workbook.ReadRange("Sheet1", "A1:B2");

        Assert.Equal(2, result.Width);
        Assert.Equal(2, result.Height);
        Assert.Equal(ExcelCellValue.FromNumber(42),    result[0, 0]); // A1
        Assert.Equal(ExcelCellValue.FromText("Hello"), result[0, 1]); // B1
        Assert.Equal(ExcelCellValue.FromNumber(3.14),  result[1, 0]); // A2
        Assert.Equal(ExcelCellValue.FromBoolean(true), result[1, 1]); // B2
    }

    [Fact]
    public void ReadCell_KnownValue_DecodesCorrectly()
    {
        using var ms = CreateWorkbook();
        using var workbook = XLSight.ExcelWorkbook.Open(ms);

        var result = workbook.ReadCell("Sheet1", "A1");

        Assert.Equal(ExcelCellValue.FromNumber(42), result.Value);
        Assert.Equal(1, result.Row);
        Assert.Equal(1, result.Column);
    }

    [Fact]
    public void ReadRange_UnknownSheet_ThrowsSheetNotFoundException()
    {
        using var ms = CreateWorkbook();
        using var workbook = XLSight.ExcelWorkbook.Open(ms);

        Assert.Throws<SheetNotFoundException>(() => workbook.ReadRange("NoSuchSheet", "A1:B2"));
    }

    [Fact]
    public void ReadRange_InvalidAddress_ThrowsInvalidAddressException()
    {
        using var ms = CreateWorkbook();
        using var workbook = XLSight.ExcelWorkbook.Open(ms);

        Assert.Throws<InvalidAddressException>(() => workbook.ReadRange("Sheet1", "NOTANADDRESS"));
    }

    [Fact]
    public void ReadRange_AfterDispose_ThrowsObjectDisposedException()
    {
        using var ms = CreateWorkbook();
        var workbook = XLSight.ExcelWorkbook.Open(ms);
        workbook.Dispose();

        Assert.Throws<ObjectDisposedException>(() => workbook.ReadRange("Sheet1", "A1:B2"));
    }

    private sealed class NonSeekableStream(Stream inner) : Stream
    {
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override void Flush() => inner.Flush();
        public override int Read(byte[] buffer, int offset, int count) => inner.Read(buffer, offset, count);
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
