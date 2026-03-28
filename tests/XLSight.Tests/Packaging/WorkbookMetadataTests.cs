using System.IO.Compression;
using System.Text;
using Xunit;
using XLSight.Exceptions;
using XLSight.Internal.Packaging;

namespace XLSight.Tests.Packaging;

public sealed class WorkbookMetadataTests
{
    [Fact]
    public void WorkbookParser_Parse_MultipleSheets_PreservesNamesAndOrder()
    {
        using var stream = CreateUtf8Stream("""
            <workbook xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main"
                      xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships">
              <sheets>
                <sheet name="Summary" sheetId="1" r:id="rId1" />
                <sheet name="Data" sheetId="2" r:id="rId2" />
                <sheet name="Archive" sheetId="3" r:id="rId3" />
              </sheets>
            </workbook>
            """);

        WorkbookParser.ParsedWorkbookDefinition workbook = WorkbookParser.Parse(stream);

        Assert.Collection(
            workbook.Sheets,
            sheet => Assert.Equal(("Summary", "rId1"), (sheet.Name, sheet.RelationshipId)),
            sheet => Assert.Equal(("Data", "rId2"), (sheet.Name, sheet.RelationshipId)),
            sheet => Assert.Equal(("Archive", "rId3"), (sheet.Name, sheet.RelationshipId)));
    }

    [Fact]
    public void WorkbookParser_Parse_AcceptsAlternateRelationshipIdPrefix()
    {
        using var stream = CreateUtf8Stream("""
            <workbook xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main"
                      xmlns:relationships="http://schemas.openxmlformats.org/officeDocument/2006/relationships">
              <sheets>
                <sheet name="Summary" sheetId="1" relationships:id="rId1" />
              </sheets>
            </workbook>
            """);

        WorkbookParser.ParsedWorkbookDefinition workbook = WorkbookParser.Parse(stream);

        var sheet = Assert.Single(workbook.Sheets);
        Assert.Equal(("Summary", "rId1"), (sheet.Name, sheet.RelationshipId));
    }

    [Fact]
    public void WorkbookParser_Parse_ExtractsNamedRangesAndDate1904()
    {
        using var stream = CreateUtf8Stream("""
            <workbook xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main"
                      xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships">
              <workbookPr date1904="1" />
              <sheets>
                <sheet name="Summary" sheetId="1" r:id="rId1" />
                <sheet name="Data" sheetId="2" r:id="rId2" />
              </sheets>
              <definedNames>
                <definedName name="GlobalTotals">Summary!$A$1:$C$10</definedName>
                <definedName name="LocalFilter" localSheetId="1">Data!$A$1:$A$20</definedName>
              </definedNames>
            </workbook>
            """);

        WorkbookParser.ParsedWorkbookDefinition workbook = WorkbookParser.Parse(stream, hasMacros: true);

        Assert.True(workbook.UsesDate1904);
        Assert.True(workbook.HasMacros);
        Assert.Collection(
            workbook.NamedRanges,
            range => Assert.Equal(
                new WorkbookMetadata.WorkbookNamedRange("GlobalTotals", "Summary!$A$1:$C$10", null),
                range),
            range => Assert.Equal(
                new WorkbookMetadata.WorkbookNamedRange("LocalFilter", "Data!$A$1:$A$20", "Data"),
                range));
    }

    [Fact]
    public void WorkbookParser_Parse_MissingOptionalElements_IsGraceful()
    {
        using var stream = CreateUtf8Stream("""
            <workbook xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main">
              <sheets />
            </workbook>
            """);

        WorkbookParser.ParsedWorkbookDefinition workbook = WorkbookParser.Parse(stream);

        Assert.False(workbook.UsesDate1904);
        Assert.False(workbook.HasMacros);
        Assert.Empty(workbook.Sheets);
        Assert.Empty(workbook.NamedRanges);
    }

    [Fact]
    public void RelationshipsParser_Parse_ResolvesSheetPaths()
    {
        using var workbookStream = CreateUtf8Stream("""
            <workbook xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main"
                      xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships">
              <sheets>
                <sheet name="Summary" sheetId="1" r:id="rId1" />
                <sheet name="Data" sheetId="2" r:id="rId2" />
              </sheets>
            </workbook>
            """);
        using var relationshipsStream = CreateUtf8Stream("""
            <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
              <Relationship Id="rId1" Target="worksheets/sheet1.xml" />
              <Relationship Id="rId2" Target="/xl/worksheets/sheet2.xml" />
            </Relationships>
            """);

        WorkbookParser.ParsedWorkbookDefinition workbook = WorkbookParser.Parse(workbookStream);
        WorkbookMetadata metadata = RelationshipsParser.Parse(relationshipsStream, workbook);

        Assert.Collection(
            metadata.Sheets,
            sheet => Assert.Equal(("Summary", "xl/worksheets/sheet1.xml"), (sheet.Name, sheet.Path)),
            sheet => Assert.Equal(("Data", "xl/worksheets/sheet2.xml"), (sheet.Name, sheet.Path)));
    }

    [Fact]
    public void Phase1Integration_XlsxPackage_ParseMetadata_VerifiesSheetNamesAndPaths()
    {
        using var packageStream = CreateWorkbookPackage(
            workbookXml: """
                <workbook xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main"
                          xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships">
                  <workbookPr date1904="true" />
                  <sheets>
                    <sheet name="Summary" sheetId="1" r:id="rId1" />
                    <sheet name="Data" sheetId="2" r:id="rId2" />
                  </sheets>
                  <definedNames>
                    <definedName name="Totals">Summary!$A$1:$B$5</definedName>
                  </definedNames>
                </workbook>
                """,
            relationshipsXml: """
                <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
                  <Relationship Id="rId1" Target="worksheets/sheet1.xml" />
                  <Relationship Id="rId2" Target="worksheets/sheet2.xml" />
                </Relationships>
                """);

        using XlsxPackage package = XlsxPackage.Open(packageStream);
        using Stream workbookStream = package.GetEntry("xl/workbook.xml")!.Open();
        using Stream relationshipsStream = package.GetEntry("xl/_rels/workbook.xml.rels")!.Open();

        WorkbookParser.ParsedWorkbookDefinition workbook = WorkbookParser.Parse(workbookStream);
        WorkbookMetadata metadata = RelationshipsParser.Parse(relationshipsStream, workbook);

        Assert.True(metadata.UsesDate1904);
        Assert.Collection(
            metadata.Sheets,
            sheet => Assert.Equal(("Summary", "xl/worksheets/sheet1.xml"), (sheet.Name, sheet.Path)),
            sheet => Assert.Equal(("Data", "xl/worksheets/sheet2.xml"), (sheet.Name, sheet.Path)));
        Assert.Single(metadata.NamedRanges);
        Assert.Equal("Totals", metadata.NamedRanges[0].Name);
    }

    [Fact]
    public void RelationshipsParser_Parse_SheetMissingRelationship_ThrowsMalformedWorkbookException()
    {
        using var workbookStream = CreateUtf8Stream("""
            <workbook xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main"
                      xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships">
              <sheets>
                <sheet name="Sheet1" sheetId="1" r:id="rId1" />
              </sheets>
            </workbook>
            """);
        using var relationshipsStream = CreateUtf8Stream("""
            <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
            </Relationships>
            """);

        WorkbookParser.ParsedWorkbookDefinition workbook = WorkbookParser.Parse(workbookStream);

        Assert.Throws<MalformedWorkbookException>(
            () => RelationshipsParser.Parse(relationshipsStream, workbook));
    }

    private static MemoryStream CreateUtf8Stream(string xml)
    {
        return new MemoryStream(Encoding.UTF8.GetBytes(xml));
    }

    private static MemoryStream CreateWorkbookPackage(string workbookXml, string relationshipsXml)
    {
        var stream = new MemoryStream();
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            WriteEntry(archive, "xl/workbook.xml", workbookXml);
            WriteEntry(archive, "xl/_rels/workbook.xml.rels", relationshipsXml);
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
