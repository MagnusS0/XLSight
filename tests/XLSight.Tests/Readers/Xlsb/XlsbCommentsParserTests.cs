using System.IO.Compression;
using System.Text;
using XLSight.Analysis;
using XLSight.Internal.Readers.Xlsb;
using Xunit;

namespace XLSight.Tests.Readers.Xlsb;

public sealed class XlsbCommentsParserTests : IDisposable
{
    private const string CommentsRelationshipType = "http://schemas.openxmlformats.org/officeDocument/2006/relationships/comments";
    private readonly string _tempDirectory = Path.Combine(AppContext.BaseDirectory, $"XlsbComments_{Guid.NewGuid():N}");

    [Fact]
    public void Count_CommentsPart_CountsBeginCommentRecords()
    {
        using var stream = XlsbTestRecords.Stream(
            XlsbTestRecords.Record(XlsbRecordType.BrtBeginComments, []),
            XlsbTestRecords.Record(XlsbRecordType.BrtBeginCommentAuthors, []),
            XlsbTestRecords.Record(XlsbRecordType.BrtCommentAuthor, []),
            XlsbTestRecords.Record(XlsbRecordType.BrtBeginCommentList, []),
            BeginComment(),
            XlsbTestRecords.Record(XlsbRecordType.BrtCommentText, [1, 2, 3]),
            XlsbTestRecords.Record(XlsbRecordType.BrtEndComment, []),
            BeginComment());

        int count = XlsbCommentsParser.Count(stream);

        Assert.Equal(2, count);
    }

    [Fact]
    public void Count_CorruptCommentsPart_ReturnsZero()
    {
        byte[] truncated = XlsbTestRecords.Record(XlsbRecordType.BrtBeginComment, [1]);
        Array.Resize(ref truncated, truncated.Length - 1);
        using var stream = new MemoryStream(truncated);

        int count = XlsbCommentsParser.Count(stream);

        Assert.Equal(0, count);
    }

    [Fact]
    public void Analyze_XlsbWorkbookWithCommentsRelationship_CountsComments()
    {
        string path = WriteWorkbook(
            "comments.xlsb",
            sheetRelationshipXml: $"""
                <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
                  <Relationship Id="rId1" Type="{CommentsRelationshipType}" Target="../comments1.bin" />
                </Relationships>
                """,
            commentsPart: XlsbTestRecords.Stream(BeginComment(), BeginComment(), BeginComment()));

        using var workbook = ExcelWorkbook.Open(path);
        WorkbookInfo info = workbook.Analyze(AnalysisLevel.Exact, maxDegreeOfParallelism: 1);

        SheetInfo sheet = Assert.Single(info.Sheets);
        Assert.Equal(3, sheet.Exact.CommentCount);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Analyze_MissingOrCorruptCommentsPart_ReturnsZeroCommentCount(bool includeCorruptCommentsPart)
    {
        Stream? commentsPart = includeCorruptCommentsPart
            ? new MemoryStream([0xFB, 0x04, 0x05])
            : null;
        using (commentsPart)
        {
            string path = WriteWorkbook(
                includeCorruptCommentsPart ? "corrupt-comments.xlsb" : "missing-comments.xlsb",
                sheetRelationshipXml: $"""
                    <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
                      <Relationship Id="rId1" Type="{CommentsRelationshipType}" Target="../comments1.bin" />
                    </Relationships>
                    """,
                commentsPart);

            using var workbook = ExcelWorkbook.Open(path);
            WorkbookInfo info = workbook.Analyze(AnalysisLevel.Exact, maxDegreeOfParallelism: 1);

            SheetInfo sheet = Assert.Single(info.Sheets);
            Assert.Equal(0, sheet.Exact.CommentCount);
        }
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDirectory))
        {
            Directory.Delete(_tempDirectory, recursive: true);
        }
    }

    private string WriteWorkbook(string fileName, string sheetRelationshipXml, Stream? commentsPart)
    {
        Directory.CreateDirectory(_tempDirectory);
        string path = Path.Combine(_tempDirectory, fileName);
        using var archive = ZipFile.Open(path, ZipArchiveMode.Create);

        WriteEntry(archive, "xl/workbook.bin", XlsbTestRecords.Stream(BundleSheet()));
        WriteEntry(archive, "xl/_rels/workbook.bin.rels", """
            <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
              <Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet" Target="worksheets/sheet1.bin" />
            </Relationships>
            """);
        WriteEntry(archive, "xl/worksheets/sheet1.bin", XlsbTestRecords.Stream(XlsbTestRecords.EndSheetData()));
        WriteEntry(archive, "xl/worksheets/_rels/sheet1.bin.rels", sheetRelationshipXml);
        if (commentsPart is not null)
        {
            WriteEntry(archive, "xl/comments1.bin", commentsPart);
        }

        return path;
    }

    private static byte[] BeginComment() =>
        XlsbTestRecords.Record(XlsbRecordType.BrtBeginComment, new byte[36]);

    private static byte[] BundleSheet()
    {
        byte[] relationshipId = XlsbTestRecords.WideString("rId1");
        byte[] sheetName = XlsbTestRecords.WideString("Sheet1");
        byte[] payload = new byte[8 + relationshipId.Length + sheetName.Length];
        relationshipId.CopyTo(payload.AsSpan(8));
        sheetName.CopyTo(payload.AsSpan(8 + relationshipId.Length));
        return XlsbTestRecords.Record(XlsbRecordType.BrtBundleSh, payload);
    }

    private static void WriteEntry(ZipArchive archive, string path, string content)
    {
        ZipArchiveEntry entry = archive.CreateEntry(path);
        using Stream stream = entry.Open();
        using var writer = new StreamWriter(stream, Encoding.UTF8);
        writer.Write(content);
    }

    private static void WriteEntry(ZipArchive archive, string path, Stream content)
    {
        ZipArchiveEntry entry = archive.CreateEntry(path);
        using Stream destination = entry.Open();
        content.Position = 0;
        content.CopyTo(destination);
    }
}
