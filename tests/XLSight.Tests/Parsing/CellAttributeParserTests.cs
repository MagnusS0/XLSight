using System.Text;
using XLSight.Internal.Readers.Xlsx;
using XLSight.Internal.Sinks;
using Xunit;

namespace XLSight.Tests.Parsing;

/// <summary>
/// The <c>&lt;c&gt;</c> attribute fast path must return exactly what the general tokenizer
/// returns whenever it accepts input, and must decline every shape it does not model.
/// </summary>
public sealed class CellAttributeParserTests
{
    [Theory]
    [InlineData(" r=\"A1\">", 1, 1, (int)CellDataKind.Number, 0, false)]
    [InlineData(" r=\"A1\"/>", 1, 1, (int)CellDataKind.Number, 0, true)]
    [InlineData(" r=\"XFD1048576\" s=\"12\" t=\"s\">", 16384, 1048576, (int)CellDataKind.SharedString, 12, false)]
    [InlineData(" r=\"B7\" t=\"n\" s=\"3\">", 2, 7, (int)CellDataKind.Number, 3, false)]
    [InlineData(" t=\"inlineStr\" r=\"C2\">", 3, 2, (int)CellDataKind.InlineString, 0, false)]
    [InlineData(" r=\"D4\" t=\"str\">", 4, 4, (int)CellDataKind.FormulaString, 0, false)]
    [InlineData(" r=\"D4\" t=\"d\">", 4, 4, (int)CellDataKind.FormulaString, 0, false)]
    [InlineData(" r=\"E5\" t=\"b\">", 5, 5, (int)CellDataKind.Boolean, 0, false)]
    [InlineData(" r=\"E5\" t=\"e\">", 5, 5, (int)CellDataKind.Error, 0, false)]
    [InlineData(" r=\"A0\">", 1, 0, (int)CellDataKind.Number, 0, false)]
    [InlineData(" s=\"007\">", 0, 0, (int)CellDataKind.Number, 7, false)]
    [InlineData(" r=\"A1\" r=\"B2\">", 2, 2, (int)CellDataKind.Number, 0, false)]
    [InlineData(">", 0, 0, (int)CellDataKind.Number, 0, false)]
    [InlineData("/>", 0, 0, (int)CellDataKind.Number, 0, true)]
    public void CommonShapes_AreAcceptedAndMatchGeneralParser(
        string attrs, int column, int row, int kind, int style, bool empty)
    {
        byte[] bytes = Encoding.ASCII.GetBytes(attrs);

        Assert.True(CellAttributeParser.TryParseCommonCellAttrs(bytes, out int c, out int r, out var k, out int s, out bool e));
        Assert.Equal((column, row, kind, style, empty), (c, r, (int)k, s, e));
        AssertMatchesGeneral(bytes);
    }

    [Theory]
    [InlineData(" r='A1'>")]                       // single quotes
    [InlineData("  r=\"A1\">")]                     // double space
    [InlineData("\tr=\"A1\">")]                     // tab
    [InlineData("\nr=\"A1\" t=\"s\">")]             // newline
    [InlineData(" r = \"A1\">")]                    // spaces around '='
    [InlineData(" r=\"A1\" >")]                     // space before '>'
    [InlineData(" r=\"A1\" />")]                    // space before '/>'
    [InlineData(" x:r=\"A1\">")]                    // prefixed attribute
    [InlineData(" cm=\"1\" r=\"A1\">")]             // other attribute
    [InlineData(" r=\"A1\" vm=\"1\">")]             // other attribute after r
    [InlineData(" r=\"a1\">")]                      // lowercase column
    [InlineData(" r=\"$A$1\">")]                    // absolute reference
    [InlineData(" r=\"A\">")]                       // no row digits
    [InlineData(" r=\"1\">")]                       // no column letters
    [InlineData(" r=\"AAAA1\">")]                   // four letters
    [InlineData(" r=\"A12345678\">")]               // eight digits
    [InlineData(" r=\"A99999999999\">")]            // overflowing row
    [InlineData(" r=\"A1x\">")]                     // trailing byte
    [InlineData(" r=\"A-1\">")]                     // signed row
    [InlineData(" r=\"\">")]                        // empty value
    [InlineData(" s=\"\">")]
    [InlineData(" s=\"-1\">")]
    [InlineData(" s=\"1234567890\">")]              // ten digits
    [InlineData(" s=\"1&#48;\">")]                  // entity-encoded
    [InlineData(" t=\"&#115;\">")]
    [InlineData(" t=\"S\">")]
    [InlineData(" t=\"x\">")]
    [InlineData(" t=\"\">")]
    [InlineData(" t=\"inlinestr\">")]
    [InlineData(" t=\"strx\">")]
    [InlineData(" t=\"ss\">")]
    [InlineData(" r=\"A1\"")]                       // no terminator
    [InlineData(" r=\"A1")]                         // unterminated value
    [InlineData(" r=\"A1\"/")]
    [InlineData(" r=\"A1\">x")]                     // bytes after '>'
    [InlineData("")]
    public void UnusualShapes_AreDeclinedAndStillParseLikeBefore(string attrs)
    {
        byte[] bytes = Encoding.ASCII.GetBytes(attrs);

        Assert.False(CellAttributeParser.TryParseCommonCellAttrs(bytes, out _, out _, out _, out _, out _));
        AssertMatchesGeneral(bytes);
    }

    [Fact]
    public void RandomAttributeLists_MatchGeneralParser()
    {
        string[] names = ["r", "s", "t", "cm", "x:r", "R"];
        string[] values =
        [
            "A1", "Z9", "AB12", "XFD1048576", "AAA1234567", "AAAA1", "A12345678", "a1", "A", "1", "", "A0",
            "s", "n", "b", "e", "d", "str", "inlineStr", "inlineStr ", "x", "st", "0", "07", "123456789",
            "1234567890", "-3", "&amp;", "A1&#48;", "2147483648",
        ];
        string[] separators = [" ", " ", " ", " ", "  ", "\t", "\n", ""];
        string[] quotes = ["\"", "\"", "\"", "'"];
        string[] endings = [">", "/>", " >", " />", "", "/"];

        var random = new Random(20261026);
        int accepted = 0;
        var builder = new StringBuilder();
        for (int i = 0; i < 200_000; i++)
        {
            builder.Clear();
            int count = random.Next(0, 5);
            for (int a = 0; a < count; a++)
            {
                string quote = quotes[random.Next(quotes.Length)];
                builder.Append(separators[random.Next(separators.Length)])
                    .Append(names[random.Next(names.Length)])
                    .Append('=')
                    .Append(quote)
                    .Append(values[random.Next(values.Length)])
                    .Append(quote);
            }

            builder.Append(endings[random.Next(endings.Length)]);
            byte[] bytes = Encoding.ASCII.GetBytes(builder.ToString());
            if (CellAttributeParser.TryParseCommonCellAttrs(bytes, out _, out _, out _, out _, out _))
            {
                accepted++;
            }

            AssertMatchesGeneral(bytes);
        }

        // The generator must exercise both paths substantially.
        Assert.InRange(accepted, 10_000, 190_000);
    }

    private static void AssertMatchesGeneral(byte[] bytes)
    {
        CellAttributeParser.ParseCellAttrs(bytes, out int c1, out int r1, out var k1, out int s1, out bool e1);
        CellAttributeParser.ParseCellAttrsGeneral(bytes, out int c2, out int r2, out var k2, out int s2, out bool e2);
        Assert.True(
            (c1, r1, k1, s1, e1) == (c2, r2, k2, s2, e2),
            $"'{Encoding.ASCII.GetString(bytes)}': fast {(c1, r1, k1, s1, e1)} vs general {(c2, r2, k2, s2, e2)}");
    }
}
