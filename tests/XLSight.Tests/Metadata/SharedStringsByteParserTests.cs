using System.Text;
using XLSight.Internal.Metadata;
using Xunit;

namespace XLSight.Tests.Metadata;

/// <summary>
/// Tests for <see cref="SharedStringsByteParser"/> covering edge cases not reached
/// by the existing <see cref="SharedStringsParserTests"/> via the delegating wrapper.
/// </summary>
public sealed class SharedStringsByteParserTests
{
    private static MemoryStream Utf8(string xml)
        => new MemoryStream(Encoding.UTF8.GetBytes(xml));

    // ── Empty SST (no <si> elements) ────────────────────────────────────────

    [Fact]
    public void Parse_SstWithNoSiElements_ReturnsEmptyTable()
    {
        using var stream = Utf8("""<sst uniqueCount="0"></sst>""");
        SharedStringTable result = SharedStringsByteParser.Parse(stream);
        Assert.Equal(0, result.Count);
    }

    [Fact]
    public void Parse_EmptySst_SelfClosingTag_ReturnsEmptyTable()
    {
        using var stream = Utf8("""<sst/>""");
        SharedStringTable result = SharedStringsByteParser.Parse(stream);
        Assert.Equal(0, result.Count);
    }

    // ── Namespace-prefixed si element ────────────────────────────────────────

    [Fact]
    public void Parse_NamespacePrefixedSi_ExtractsTextCorrectly()
    {
        // CheckBackwardContext ':' path — <x:si> where ':' precedes "si"
        using var stream = Utf8("""
            <x:sst xmlns:x="http://schemas.openxmlformats.org/spreadsheetml/2006/main">
              <x:si><t>PrefixedNamespace</t></x:si>
            </x:sst>
            """);
        SharedStringTable result = SharedStringsByteParser.Parse(stream);
        Assert.Equal(1, result.Count);
        Assert.Equal("PrefixedNamespace", result.GetString(0));
    }

    [Fact]
    public void Parse_NamespacePrefixedSiMultiple_ExtractsAll()
    {
        using var stream = Utf8("""
            <x:sst xmlns:x="http://schemas.openxmlformats.org/spreadsheetml/2006/main">
              <x:si><t>Alpha</t></x:si>
              <x:si><t>Beta</t></x:si>
              <x:si><t>Gamma</t></x:si>
            </x:sst>
            """);
        SharedStringTable result = SharedStringsByteParser.Parse(stream);
        Assert.Equal(3, result.Count);
        Assert.Equal("Alpha", result.GetString(0));
        Assert.Equal("Beta", result.GetString(1));
        Assert.Equal("Gamma", result.GetString(2));
    }

    // ── XML entities inside <t> ──────────────────────────────────────────────

    [Fact]
    public void Parse_XmlEntitiesInT_StoredRawAndDecodedOnRead()
    {
        using var stream = Utf8("""
            <sst>
              <si><t>A &amp; B</t></si>
              <si><t>&lt;tag&gt;</t></si>
            </sst>
            """);
        SharedStringTable result = SharedStringsByteParser.Parse(stream);
        Assert.Equal(2, result.Count);
        Assert.Equal("A & B", result.GetString(0));
        Assert.Equal("<tag>", result.GetString(1));
    }

    // ── <t xml:space="preserve"> ─────────────────────────────────────────────

    [Fact]
    public void Parse_TWithXmlSpacePreserve_CapturesSpaces()
    {
        // The byte parser doesn't special-case xml:space — it reads text after '>'
        using var stream = Utf8("""
            <sst>
              <si><t xml:space="preserve">  spaces  </t></si>
            </sst>
            """);
        SharedStringTable result = SharedStringsByteParser.Parse(stream);
        Assert.Equal(1, result.Count);
        Assert.Equal("  spaces  ", result.GetString(0));
    }

    // ── Self-closing <si/> mixed with regular ────────────────────────────────

    [Fact]
    public void Parse_MixedEmptyAndNonEmptySi_ProducesCorrectEntries()
    {
        using var stream = Utf8("""
            <sst>
              <si/>
              <si><t>Second</t></si>
              <si/>
              <si><t>Fourth</t></si>
            </sst>
            """);
        SharedStringTable result = SharedStringsByteParser.Parse(stream);
        Assert.Equal(4, result.Count);
        Assert.Equal("", result.GetString(0));
        Assert.Equal("Second", result.GetString(1));
        Assert.Equal("", result.GetString(2));
        Assert.Equal("Fourth", result.GetString(3));
    }

    // ── Rich text runs (<r><rPr/><t>) ────────────────────────────────────────

    [Fact]
    public void Parse_RichTextWithFormattingRuns_ConcatenatesTextOnly()
    {
        using var stream = Utf8("""
            <sst>
              <si>
                <r><rPr><b/></rPr><t>Bold</t></r>
                <r><t> and italic</t></r>
              </si>
            </sst>
            """);
        SharedStringTable result = SharedStringsByteParser.Parse(stream);
        Assert.Equal(1, result.Count);
        Assert.Equal("Bold and italic", result.GetString(0));
    }

    // ── Large number of entries (exercises EnsureInfoCapacity) ───────────────

    [Fact]
    public void Parse_ManyEntries_AllParsedCorrectly()
    {
        const int count = 2048;
        var sb = new StringBuilder();
        sb.AppendLine("<sst>");
        for (int i = 0; i < count; i++)
        {
            sb.AppendLine(System.Globalization.CultureInfo.InvariantCulture, $"<si><t>Entry{i}</t></si>");
        }
        sb.AppendLine("</sst>");

        using var stream = Utf8(sb.ToString());
        SharedStringTable result = SharedStringsByteParser.Parse(stream);

        Assert.Equal(count, result.Count);
        Assert.Equal("Entry0", result.GetString(0));
        Assert.Equal($"Entry{count - 1}", result.GetString(count - 1));
    }

    // ── Deeply nested rich text (exercises ProcessSiContent closing tag scan) ─

    [Fact]
    public void Parse_SiWithOnlyRPrAndNoT_ProducesEmptyString()
    {
        using var stream = Utf8("""
            <sst>
              <si><r><rPr><b/><i/></rPr></r></si>
            </sst>
            """);
        SharedStringTable result = SharedStringsByteParser.Parse(stream);
        Assert.Equal(1, result.Count);
        Assert.Equal("", result.GetString(0));
    }

    // ── Single character strings ──────────────────────────────────────────────

    [Fact]
    public void Parse_SingleCharacterStrings_AllDistinct()
    {
        using var stream = Utf8("""
            <sst>
              <si><t>a</t></si>
              <si><t>b</t></si>
              <si><t>c</t></si>
            </sst>
            """);
        SharedStringTable result = SharedStringsByteParser.Parse(stream);
        Assert.Equal(3, result.Count);
        Assert.Equal("a", result.GetString(0));
        Assert.Equal("b", result.GetString(1));
        Assert.Equal("c", result.GetString(2));
    }

    // ── Long string content (exercises arena growth) ─────────────────────────

    [Fact]
    public void Parse_VeryLongString_CapturedCorrectly()
    {
        string longText = new string('x', 8192);
        using var stream = Utf8($"<sst><si><t>{longText}</t></si></sst>");
        SharedStringTable result = SharedStringsByteParser.Parse(stream);
        Assert.Equal(1, result.Count);
        Assert.Equal(longText, result.GetString(0));
    }

    // ── Completely empty stream ───────────────────────────────────────────────

    [Fact]
    public void Parse_EmptyByteStream_ReturnsEmptyTable()
    {
        // Exercises the buf.IsExhausted path at the top of FindNextSiCandidate
        using var stream = new MemoryStream(Array.Empty<byte>());
        SharedStringTable result = SharedStringsByteParser.Parse(stream);
        Assert.Equal(0, result.Count);
    }

    // ── Malformed: unclosed <si> element ─────────────────────────────────────

    [Fact]
    public void Parse_UnclosedSiElement_GracefullyReturnsPartialData()
    {
        // Exercises the buf.IsExhausted break in ProcessSiContent
        using var stream = Utf8("""<sst><si><t>partial</t>""");
        SharedStringTable result = SharedStringsByteParser.Parse(stream);
        Assert.Equal(1, result.Count);
        Assert.Equal("partial", result.GetString(0));
    }

    // ── <si> with attribute containing '/' not followed by '>' ────────────────

    [Fact]
    public void Parse_SiWithSlashInAttribute_ParsesCorrectly()
    {
        // Exercises TryConsumeEmptyClose lines 182-183: '/' inside attribute value
        using var stream = Utf8("""
            <sst>
              <si style="a/b"><t>WithSlash</t></si>
            </sst>
            """);
        SharedStringTable result = SharedStringsByteParser.Parse(stream);
        Assert.Equal(1, result.Count);
        Assert.Equal("WithSlash", result.GetString(0));
    }

    // ── <t> not followed by a tag-name boundary ──────────────────────────────

    [Fact]
    public void Parse_NonTTagStartingWithT_IsSkipped()
    {
        // Exercises TryHandleTTag lines 297-298: span[ltIdx+2] not a boundary char (e.g. 'a' in <tab>)
        using var stream = Utf8("""
            <sst>
              <si><tab>ignored</tab><t>real</t></si>
            </sst>
            """);
        SharedStringTable result = SharedStringsByteParser.Parse(stream);
        Assert.Equal(1, result.Count);
        Assert.Equal("real", result.GetString(0));
    }

    // ── Stream with content that avoids CanReadMore branch ────────────────────

    [Fact]
    public void Parse_ContentWithNoClosingTags_ReturnsEmpty()
    {
        // Buffer content has no '<' after content; CanReadMore false path in ProcessSiContent
        using var stream = Utf8("<sst><si>text without tags");
        SharedStringTable result = SharedStringsByteParser.Parse(stream);
        Assert.Equal(1, result.Count);
        Assert.Equal("", result.GetString(0)); // No <t> content found
    }
}
