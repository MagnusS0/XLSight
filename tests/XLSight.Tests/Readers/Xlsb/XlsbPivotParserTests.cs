using System.IO.Compression;
using System.Globalization;
using System.Text;
using XLSight.Analysis;
using XLSight.Internal.Packaging;
using XLSight.Internal.Readers.Xlsb;
using Xunit;

namespace XLSight.Tests.Readers.Xlsb;

public sealed class XlsbPivotParserTests
{
    [Fact]
    public void ParsePivotTable_ReadsNameCacheIdAndLocation()
    {
        using var stream = XlsbTestRecords.Stream(
            XlsbTestRecords.BeginSxView(7, "PivotTable1"),
            XlsbTestRecords.BeginSxLocation(4, 2, 12, 6));

        XlsbPivotParser.PivotTableMetadata pivot = XlsbPivotParser.ParsePivotTable(stream);

        Assert.Equal("PivotTable1", pivot.Name);
        Assert.Equal(7u, pivot.CacheId);
        Assert.Equal(new ExcelRange(new ExcelAddress(2, 4), new ExcelAddress(6, 12)), pivot.Range);
    }

    [Fact]
    public void ParsePivotCacheSource_WithLocalRange_ReturnsSheetReference()
    {
        using var stream = XlsbTestRecords.Stream(
            XlsbTestRecords.BeginPcdSource(0),
            XlsbTestRecords.BeginPcdsRange("Data", "A1:B10"));

        string? source = XlsbPivotParser.ParsePivotCacheSource(stream);

        Assert.Equal("Data!A1:B10", source);
    }

    [Fact]
    public void ParsePivotCacheSource_WithExternalRange_ReturnsNull()
    {
        using var stream = XlsbTestRecords.Stream(
            XlsbTestRecords.BeginPcdSource(0),
            XlsbTestRecords.BeginExternalPcdsRange("rIdExternal", "A1:B10"));

        string? source = XlsbPivotParser.ParsePivotCacheSource(stream);

        Assert.Null(source);
    }

    [Fact]
    public void Analyze_WithSheetPivotRelationship_AddsWorkbookAndSheetPivotMetadata()
    {
        using var packageStream = CreatePackage(
            ("xl/workbook.bin", XlsbTestRecords.Record(XlsbRecordType.BrtBeginBook, [])),
            ("xl/worksheets/sheet1.bin", XlsbTestRecords.Stream(
                XlsbTestRecords.Record(XlsbRecordType.BrtBeginSheetData, []),
                XlsbTestRecords.EndSheetData()).ToArray()),
            ("xl/worksheets/_rels/sheet1.bin.rels", RelationshipsXml(
                ("rIdPivot1", "http://schemas.openxmlformats.org/officeDocument/2006/relationships/pivotTable", "../pivotTables/pivotTable1.bin"))),
            ("xl/pivotTables/pivotTable1.bin", XlsbTestRecords.Stream(
                XlsbTestRecords.BeginSxView(99, "SalesPivot"),
                XlsbTestRecords.BeginSxLocation(3, 4, 9, 8)).ToArray()),
            ("xl/pivotTables/_rels/pivotTable1.bin.rels", RelationshipsXml(
                ("rIdCache1", "http://schemas.openxmlformats.org/officeDocument/2006/relationships/pivotCacheDefinition", "../pivotCache/pivotCacheDefinition1.bin"))),
            ("xl/pivotCache/pivotCacheDefinition1.bin", XlsbTestRecords.Stream(
                XlsbTestRecords.BeginPcdSource(0),
                XlsbTestRecords.BeginPcdsRange("Data", "A1:D100")).ToArray()));
        using XlsxPackage package = XlsxPackage.Open(packageStream);
        var reader = new XlsbWorkbookReader(
            package,
            new XlsbMetadata([new XlsbSheetInfo("Pivot", "xl/worksheets/sheet1.bin")], usesDate1904: false));

        WorkbookInfo info = reader.Analyze(AnalysisLevel.Exact, maxDegreeOfParallelism: 1);

        PivotTableInfo workbookPivot = Assert.Single(info.Exact.PivotTables);
        Assert.Equal("SalesPivot", workbookPivot.Name);
        Assert.Equal("Pivot", workbookPivot.Sheet);
        Assert.Equal(new ExcelRange(new ExcelAddress(4, 3), new ExcelAddress(8, 9)), workbookPivot.Range);
        Assert.Equal("Data!A1:D100", workbookPivot.SourceReference);

        SheetInfo sheet = Assert.Single(info.Sheets);
        PivotTableInfo sheetPivot = Assert.Single(sheet.Exact.PivotTables);
        Assert.Same(workbookPivot, sheetPivot);
    }

    [Fact]
    public void Analyze_UsesWorkbookPivotCacheRelationshipWhenPivotRelationshipIsMissing()
    {
        using var packageStream = CreatePackage(
            ("xl/workbook.bin", XlsbTestRecords.Stream(
                XlsbTestRecords.Record(XlsbRecordType.BrtBeginBook, []),
                XlsbTestRecords.BeginPivotCacheId(42, "rIdCache42")).ToArray()),
            ("xl/_rels/workbook.bin.rels", RelationshipsXml(
                ("rIdCache42", "http://schemas.openxmlformats.org/officeDocument/2006/relationships/pivotCacheDefinition", "pivotCache/pivotCacheDefinition42.bin"))),
            ("xl/worksheets/sheet1.bin", XlsbTestRecords.Stream(
                XlsbTestRecords.Record(XlsbRecordType.BrtBeginSheetData, []),
                XlsbTestRecords.EndSheetData()).ToArray()),
            ("xl/worksheets/_rels/sheet1.bin.rels", RelationshipsXml(
                ("rIdPivot1", "http://schemas.openxmlformats.org/officeDocument/2006/relationships/pivotTable", "../pivotTables/pivotTable1.bin"))),
            ("xl/pivotTables/pivotTable1.bin", XlsbTestRecords.Stream(
                XlsbTestRecords.BeginSxView(42, "WorkbookCachePivot")).ToArray()),
            ("xl/pivotCache/pivotCacheDefinition42.bin", XlsbTestRecords.Stream(
                XlsbTestRecords.BeginPcdSource(0),
                XlsbTestRecords.BeginPcdsRange("Data", "C5:E20")).ToArray()));
        using XlsxPackage package = XlsxPackage.Open(packageStream);
        var reader = new XlsbWorkbookReader(
            package,
            new XlsbMetadata([new XlsbSheetInfo("Pivot", "xl/worksheets/sheet1.bin")], usesDate1904: false));

        WorkbookInfo info = reader.Analyze(AnalysisLevel.Exact, maxDegreeOfParallelism: 1);

        PivotTableInfo pivot = Assert.Single(info.Exact.PivotTables);
        Assert.Equal("WorkbookCachePivot", pivot.Name);
        Assert.Equal("Data!C5:E20", pivot.SourceReference);
    }

    private static MemoryStream CreatePackage(params (string Path, object Content)[] entries)
    {
        var stream = new MemoryStream();
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach ((string path, object content) in entries)
            {
                ZipArchiveEntry entry = archive.CreateEntry(path);
                using Stream entryStream = entry.Open();
                if (content is byte[] bytes)
                {
                    entryStream.Write(bytes);
                    continue;
                }

                using var writer = new StreamWriter(entryStream, Encoding.UTF8, leaveOpen: true);
                writer.Write((string)content);
            }
        }

        stream.Position = 0;
        return stream;
    }

    private static string RelationshipsXml(
        params (string Id, string Type, string Target)[] relationships)
    {
        var builder = new StringBuilder("""<?xml version="1.0" encoding="UTF-8"?><Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">""");
        foreach ((string id, string type, string target) in relationships)
        {
            builder.Append(CultureInfo.InvariantCulture, $"""<Relationship Id="{id}" Type="{type}" Target="{target}"/>""");
        }

        builder.Append("</Relationships>");
        return builder.ToString();
    }
}
