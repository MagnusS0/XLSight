using System.Text;
using XLSight.Internal.Packaging;
using Xunit;

namespace XLSight.Tests.Packaging;

/// <summary>
/// Additional tests for <see cref="WorkbookParser"/> covering paths not
/// reached by the main WorkbookMetadataTests.
/// </summary>
public sealed class WorkbookParserAdditionalTests
{
    private static MemoryStream Utf8(string xml)
        => new MemoryStream(Encoding.UTF8.GetBytes(xml));

    // ── date1904="true" (string, case-insensitive) ────────────────────────────

    [Fact]
    public void Parse_Date1904AsStringTrue_ReturnsTrue()
    {
        using var stream = Utf8("""
            <workbook xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main"
                      xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships">
              <workbookPr date1904="true" />
              <sheets>
                <sheet name="Sheet1" sheetId="1" r:id="rId1" />
              </sheets>
            </workbook>
            """);
        WorkbookParser.ParsedWorkbookDefinition wb = WorkbookParser.Parse(stream);
        Assert.True(wb.UsesDate1904);
    }

    [Fact]
    public void Parse_Date1904AsTRUE_CaseInsensitive_ReturnsTrue()
    {
        using var stream = Utf8("""
            <workbook xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main"
                      xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships">
              <workbookPr date1904="TRUE" />
              <sheets>
                <sheet name="Sheet1" sheetId="1" r:id="rId1" />
              </sheets>
            </workbook>
            """);
        WorkbookParser.ParsedWorkbookDefinition wb = WorkbookParser.Parse(stream);
        Assert.True(wb.UsesDate1904);
    }

    [Fact]
    public void Parse_Date1904AsZero_ReturnsFalse()
    {
        using var stream = Utf8("""
            <workbook xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main"
                      xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships">
              <workbookPr date1904="0" />
              <sheets>
                <sheet name="Sheet1" sheetId="1" r:id="rId1" />
              </sheets>
            </workbook>
            """);
        WorkbookParser.ParsedWorkbookDefinition wb = WorkbookParser.Parse(stream);
        Assert.False(wb.UsesDate1904);
    }

    [Fact]
    public void Parse_Date1904AsFalse_ReturnsFalse()
    {
        using var stream = Utf8("""
            <workbook xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main"
                      xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships">
              <workbookPr date1904="false" />
              <sheets>
                <sheet name="Sheet1" sheetId="1" r:id="rId1" />
              </sheets>
            </workbook>
            """);
        WorkbookParser.ParsedWorkbookDefinition wb = WorkbookParser.Parse(stream);
        Assert.False(wb.UsesDate1904);
    }

    // ── workbookPr without date1904 attribute ─────────────────────────────────

    [Fact]
    public void Parse_WorkbookPrWithoutDate1904_ReturnsFalse()
    {
        using var stream = Utf8("""
            <workbook xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main"
                      xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships">
              <workbookPr showHorizontalScroll="1" />
              <sheets>
                <sheet name="Sheet1" sheetId="1" r:id="rId1" />
              </sheets>
            </workbook>
            """);
        WorkbookParser.ParsedWorkbookDefinition wb = WorkbookParser.Parse(stream);
        Assert.False(wb.UsesDate1904);
    }

    // ── hasMacros flag ────────────────────────────────────────────────────────

    [Fact]
    public void Parse_WithHasMacros_PropagatesFlag()
    {
        using var stream = Utf8("""
            <workbook xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main"
                      xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships">
              <sheets>
                <sheet name="Sheet1" sheetId="1" r:id="rId1" />
              </sheets>
            </workbook>
            """);
        WorkbookParser.ParsedWorkbookDefinition wb = WorkbookParser.Parse(stream, hasMacros: true);
        Assert.True(wb.HasMacros);
    }

    // ── Sheet with missing name skipped ──────────────────────────────────────

    [Fact]
    public void Parse_SheetWithMissingName_IsSkipped()
    {
        using var stream = Utf8("""
            <workbook xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main"
                      xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships">
              <sheets>
                <sheet sheetId="1" r:id="rId1" />
                <sheet name="Good" sheetId="2" r:id="rId2" />
              </sheets>
            </workbook>
            """);
        WorkbookParser.ParsedWorkbookDefinition wb = WorkbookParser.Parse(stream);
        var sheet = Assert.Single(wb.Sheets);
        Assert.Equal("Good", sheet.Name);
    }

    [Fact]
    public void Parse_SheetWithMissingRelationshipId_IsSkipped()
    {
        using var stream = Utf8("""
            <workbook xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main">
              <sheets>
                <sheet name="NoRelId" sheetId="1" />
                <sheet name="Good" sheetId="2" id="rId2" />
              </sheets>
            </workbook>
            """);
        WorkbookParser.ParsedWorkbookDefinition wb = WorkbookParser.Parse(stream);
        // "Good" uses plain "id" attribute (GetAttributeByLocalName fallback)
        Assert.Single(wb.Sheets);
    }

    // ── No sheets element at all ──────────────────────────────────────────────

    [Fact]
    public void Parse_WorkbookWithNoSheetsElement_ReturnsEmptySheetList()
    {
        using var stream = Utf8("""
            <workbook xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main">
              <workbookPr date1904="1" />
            </workbook>
            """);
        WorkbookParser.ParsedWorkbookDefinition wb = WorkbookParser.Parse(stream);
        Assert.Empty(wb.Sheets);
        Assert.True(wb.UsesDate1904);
    }

    // ── definedName with empty reference (skipped) ────────────────────────────

    [Fact]
    public void Parse_DefinedNameWithEmptyReference_IsSkipped()
    {
        using var stream = Utf8("""
            <workbook xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main"
                      xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships">
              <sheets>
                <sheet name="Sheet1" sheetId="1" r:id="rId1" />
              </sheets>
              <definedNames>
                <definedName name="EmptyRef"></definedName>
                <definedName name="GoodRef">Sheet1!$A$1</definedName>
              </definedNames>
            </workbook>
            """);
        WorkbookParser.ParsedWorkbookDefinition wb = WorkbookParser.Parse(stream);
        var namedRange = Assert.Single(wb.NamedRanges);
        Assert.Equal("GoodRef", namedRange.Name);
    }

    // ── definedName with empty element (IsEmptyElement = true) ───────────────

    [Fact]
    public void Parse_EmptyDefinedNameElement_IsSkipped()
    {
        using var stream = Utf8("""
            <workbook xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main"
                      xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships">
              <sheets>
                <sheet name="Sheet1" sheetId="1" r:id="rId1" />
              </sheets>
              <definedNames>
                <definedName name="EmptyElem" />
                <definedName name="Real">Sheet1!$B$2</definedName>
              </definedNames>
            </workbook>
            """);
        WorkbookParser.ParsedWorkbookDefinition wb = WorkbookParser.Parse(stream);
        var range = Assert.Single(wb.NamedRanges);
        Assert.Equal("Real", range.Name);
    }

    // ── localSheetId out-of-range ─────────────────────────────────────────────

    [Fact]
    public void Parse_DefinedNameWithOutOfRangeLocalSheetId_ScopeIsNull()
    {
        using var stream = Utf8("""
            <workbook xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main"
                      xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships">
              <sheets>
                <sheet name="Sheet1" sheetId="1" r:id="rId1" />
              </sheets>
              <definedNames>
                <definedName name="OOBScope" localSheetId="99">Sheet1!$A$1</definedName>
              </definedNames>
            </workbook>
            """);
        WorkbookParser.ParsedWorkbookDefinition wb = WorkbookParser.Parse(stream);
        var range = Assert.Single(wb.NamedRanges);
        Assert.Null(range.ScopeSheetName);
    }

    // ── Malformed XML throws MalformedWorkbookException ───────────────────────

    [Fact]
    public void Parse_MalformedXml_ThrowsMalformedWorkbookException()
    {
        using var stream = Utf8("""<workbook><sheets><sheet name="x" r:id="rId1" <<BROKEN""");
        Assert.Throws<XLSight.Exceptions.MalformedWorkbookException>(() => WorkbookParser.Parse(stream));
    }
}
