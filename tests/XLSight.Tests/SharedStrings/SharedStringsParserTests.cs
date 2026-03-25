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

        var result = SharedStringsParser.Parse(null, names);

        Assert.Same(Array.Empty<string>(), result);
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

        var result = SharedStringsParser.Parse(stream, names);

        Assert.Equal(["Hello", "World", "!"], result);
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

        var result = SharedStringsParser.Parse(stream, names);

        Assert.Equal(["Bold Normal"], result);
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

        var result = SharedStringsParser.Parse(stream, names);

        Assert.Equal(["", "After"], result);
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

        var result = SharedStringsParser.Parse(stream, names);

        Assert.Equal(2, result.Length);
        Assert.Equal("First", result[0]);
        Assert.Equal("Second", result[1]);
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

        var result = SharedStringsParser.Parse(stream, names);

        Assert.Equal(3, result.Length);
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
        var result = SharedStringsParser.Parse(stream, names);

        Assert.Equal(2, result.Length);
        Assert.Equal("Alpha", result[0]);
        Assert.Equal("Beta", result[1]);
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

        var result = SharedStringsParser.Parse(stream, names);

        Assert.Equal(["Test"], result);
    }
}
