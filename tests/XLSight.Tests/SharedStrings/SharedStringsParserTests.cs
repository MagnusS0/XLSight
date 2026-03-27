using System.Text;
using Xunit;
using XLSight.SharedStrings;
using XLSight.Worksheets;

namespace XLSight.Tests.SharedStrings;

public sealed class SharedStringsParserTests
{
    private static MemoryStream CreateUtf8Stream(string xml)
    {
        var bytes = Encoding.UTF8.GetBytes(xml);
        return new MemoryStream(bytes);
    }

    [Fact]
    public void Parse_NullStream_ReturnsEmptyArray()
    {
        var names = new XlsxNameTable();

        var result = SharedStringsParser.Parse(null);

        Assert.Same(SharedStringTable.Empty, result);
    }

    [Fact]
    public void Parse_SimpleStrings_ReturnsCorrectArray()
    {
        var names = new XlsxNameTable();
        using var stream = CreateUtf8Stream("""
            <sst uniqueCount="3">
              <si><t>Hello</t></si>
              <si><t>World</t></si>
              <si><t>!</t></si>
            </sst>
            """);

        var result = SharedStringsParser.Parse(stream);

        Assert.Equal(3, result.Count);
        Assert.Equal("Hello", result.GetString(0));
        Assert.Equal("World", result.GetString(1));
        Assert.Equal("!", result.GetString(2));
    }

    [Fact]
    public void Parse_RichTextRuns_ConcatenatesText()
    {
        var names = new XlsxNameTable();
        using var stream = CreateUtf8Stream("""
            <sst>
              <si><r><t>Bold</t></r><r><t> Normal</t></r></si>
            </sst>
            """);

        var result = SharedStringsParser.Parse(stream);

        Assert.Equal(1, result.Count);
        Assert.Equal("Bold Normal", result.GetString(0));
    }

    [Fact]
    public void Parse_EmptySiElement_ProducesEmptyString()
    {
        var names = new XlsxNameTable();
        using var stream = CreateUtf8Stream("""
            <sst>
              <si/>
              <si><t>After</t></si>
            </sst>
            """);

        var result = SharedStringsParser.Parse(stream);

        Assert.Equal(2, result.Count);
        Assert.Equal("", result.GetString(0));
        Assert.Equal("After", result.GetString(1));
    }

    [Fact]
    public void Parse_UniqueCountHint_PreSizesCorrectly()
    {
        var names = new XlsxNameTable();
        using var stream = CreateUtf8Stream("""
            <sst uniqueCount="2">
              <si><t>First</t></si>
              <si><t>Second</t></si>
            </sst>
            """);

        var result = SharedStringsParser.Parse(stream);

        Assert.Equal(2, result.Count);
        Assert.Equal("First", result.GetString(0));
        Assert.Equal("Second", result.GetString(1));
    }

    [Fact]
    public void Parse_MismatchedUniqueCount_StillParsesAll()
    {
        var names = new XlsxNameTable();
        using var stream = CreateUtf8Stream("""
            <sst uniqueCount="1">
              <si><t>One</t></si>
              <si><t>Two</t></si>
              <si><t>Three</t></si>
            </sst>
            """);

        var result = SharedStringsParser.Parse(stream);

        Assert.Equal(3, result.Count);
    }

    [Fact]
    public void Parse_CapAtMaxSharedStringCount_StopsAfterCap()
    {
        var names = new XlsxNameTable();
        using var stream = CreateUtf8Stream("""
            <sst uniqueCount="20000000">
              <si><t>Alpha</t></si>
              <si><t>Beta</t></si>
            </sst>
            """);

        // Should not throw from over-allocation and should parse actual elements
        var result = SharedStringsParser.Parse(stream);

        Assert.Equal(2, result.Count);
        Assert.Equal("Alpha", result.GetString(0));
        Assert.Equal("Beta", result.GetString(1));
    }

    [Fact]
    public void Parse_XmlWithNamespace_ParsesCorrectly()
    {
        var names = new XlsxNameTable();
        using var stream = CreateUtf8Stream("""
            <sst xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main" uniqueCount="1">
              <si><t>Test</t></si>
            </sst>
            """);

        var result = SharedStringsParser.Parse(stream);

        Assert.Equal(1, result.Count);
        Assert.Equal("Test", result.GetString(0));
    }
}
