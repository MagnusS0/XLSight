using System.Net;
using System.Text;
using XLSight.Internal.Readers.Xlsx;
using Xunit;

namespace XLSight.Tests.Parsing;

/// <summary>
/// The single-allocation decoder must produce exactly what the historical
/// <c>WebUtility.HtmlDecode(Encoding.UTF8.GetString(bytes))</c> path produced.
/// </summary>
public sealed class XmlTextDecoderTests
{
    private static readonly string[] Fragments =
    [
        "plain", " ", "Ünïcödé", "漢字", "\U0001F600", "&amp;", "&lt;", "&gt;", "&quot;", "&apos;",
        "&#65;", "&#x41;", "&#X1F600;", "&#128512;", "&#0;", "&#xD800;", "&#x110000;", "&#;", "&#x;",
        "&#+65;", "&# 65;", "&nbsp;", "&copy;", "&unknown;", "&", "&amp", "AT&T", "&&amp;", "&;",
        "&#00000065;", "&#x0000041;", "&#99999999;", ";", "#", "x",
    ];

    [Fact]
    public void RandomEntityMixes_MatchHtmlDecodeOfUtf8String()
    {
        var random = new Random(20261026);
        var builder = new StringBuilder();
        for (int i = 0; i < 50_000; i++)
        {
            builder.Clear();
            int parts = random.Next(1, 8);
            for (int p = 0; p < parts; p++)
            {
                builder.Append(Fragments[random.Next(Fragments.Length)]);
            }

            if (random.Next(10) == 0)
            {
                builder.Append(new string('a', random.Next(250, 600)));
            }

            AssertMatchesLegacy(builder.ToString());
        }
    }

    [Theory]
    [InlineData("")]
    [InlineData("no entities")]
    [InlineData("&lt;tag a=&quot;1&quot;&gt; &amp; 'x'")]
    [InlineData("&amp;lt;")]
    public void KnownInputs_MatchHtmlDecodeOfUtf8String(string text) => AssertMatchesLegacy(text);

    [Theory]
    [InlineData("#N/A")]
    [InlineData("#DIV/0!")]
    [InlineData("#GETTING_DATA")]
    [InlineData("#CUSTOM?")]
    [InlineData("")]
    public void ErrorCodes_DecodeToEqualStrings(string code)
    {
        Assert.Equal(code, Utf8CellDecoder.DecodeErrorCode(Encoding.UTF8.GetBytes(code)));
    }

    private static void AssertMatchesLegacy(string text)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(text);
        string legacy = Encoding.UTF8.GetString(bytes);
        if (legacy.Contains('&', StringComparison.Ordinal))
        {
            legacy = WebUtility.HtmlDecode(legacy);
        }

        Assert.Equal(legacy, Utf8CellDecoder.DecodeXmlText(bytes));
    }
}
