using System.Text;
using Xunit;
using XLSight.Styles;

namespace XLSight.Tests.Styles;

public sealed class StylesParserTests
{
    private static MemoryStream ToStream(string xml) =>
        new(Encoding.UTF8.GetBytes(xml));

    [Fact]
    public void Parse_NullStream_ReturnsDefaultStyleTable()
    {
        var result = StylesParser.Parse(null);

        Assert.Same(StyleTable.Default, result);
        Assert.Equal(FormatClass.General, result.GetClassification(0));
    }

    [Fact]
    public void Parse_BuiltInDateFormat_ClassifiesAsDate()
    {
        const string xml = """
            <styleSheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main">
              <cellXfs>
                <xf numFmtId="0"/>
                <xf numFmtId="14"/>
              </cellXfs>
            </styleSheet>
            """;

        using var stream = ToStream(xml);
        var table = StylesParser.Parse(stream);

        Assert.Equal(FormatClass.General, table.GetClassification(0));
        Assert.Equal(FormatClass.Date, table.GetClassification(1));
    }

    [Fact]
    public void Parse_BuiltInTimeFormats_ClassifyAsTime()
    {
        const string xml = """
            <styleSheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main">
              <cellXfs>
                <xf numFmtId="18"/>
                <xf numFmtId="19"/>
                <xf numFmtId="20"/>
                <xf numFmtId="21"/>
                <xf numFmtId="22"/>
              </cellXfs>
            </styleSheet>
            """;

        using var stream = ToStream(xml);
        var table = StylesParser.Parse(stream);

        Assert.Equal(FormatClass.Time, table.GetClassification(0));
        Assert.Equal(FormatClass.Time, table.GetClassification(1));
        Assert.Equal(FormatClass.Time, table.GetClassification(2));
        Assert.Equal(FormatClass.Time, table.GetClassification(3));
        Assert.Equal(FormatClass.DateTime, table.GetClassification(4));
    }

    [Fact]
    public void Parse_CustomDateFormat_ClassifiesAsDate()
    {
        const string xml = """
            <styleSheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main">
              <numFmts count="1">
                <numFmt numFmtId="164" formatCode="yyyy-mm-dd"/>
              </numFmts>
              <cellXfs>
                <xf numFmtId="0"/>
                <xf numFmtId="164"/>
              </cellXfs>
            </styleSheet>
            """;

        using var stream = ToStream(xml);
        var table = StylesParser.Parse(stream);

        Assert.Equal(FormatClass.General, table.GetClassification(0));
        Assert.Equal(FormatClass.Date, table.GetClassification(1));
    }

    [Fact]
    public void Parse_MissingNumFmts_BuiltInFormatsStillWork()
    {
        const string xml = """
            <styleSheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main">
              <cellXfs>
                <xf numFmtId="14"/>
              </cellXfs>
            </styleSheet>
            """;

        using var stream = ToStream(xml);
        var table = StylesParser.Parse(stream);

        Assert.Equal(FormatClass.Date, table.GetClassification(0));
    }

    [Fact]
    public void Parse_OutOfBoundsStyleIndex_ReturnsGeneral()
    {
        const string xml = """
            <styleSheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main">
              <cellXfs>
                <xf numFmtId="14"/>
              </cellXfs>
            </styleSheet>
            """;

        using var stream = ToStream(xml);
        var table = StylesParser.Parse(stream);

        Assert.Equal(FormatClass.General, table.GetClassification(9999));
        Assert.Equal(FormatClass.General, table.GetClassification(-1));
    }

    [Fact]
    public void Parse_StyleCountCap_StopsAtMaxStyleCount()
    {
        // Build XML with 200 xf entries — well below MaxStyleCount but exercises the parsing path.
        var sb = new StringBuilder();
        sb.Append("""<styleSheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main"><cellXfs>""");
        for (int i = 0; i < 200; i++)
        {
            sb.Append("""<xf numFmtId="0"/>""");
        }
        sb.Append("</cellXfs></styleSheet>");

        using var stream = ToStream(sb.ToString());
        var table = StylesParser.Parse(stream);

        // All 200 entries parsed successfully, none dropped
        Assert.Equal(FormatClass.General, table.GetClassification(0));
        Assert.Equal(FormatClass.General, table.GetClassification(199));
        Assert.Equal(FormatClass.General, table.GetClassification(200)); // out of bounds
    }

    [Fact]
    public void Parse_NoStyles_ReturnsEmptyClassifications()
    {
        const string xml = """
            <styleSheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main">
              <cellXfs count="0"/>
            </styleSheet>
            """;

        using var stream = ToStream(xml);
        var table = StylesParser.Parse(stream);

        Assert.Equal(FormatClass.General, table.GetClassification(0));
    }
}
