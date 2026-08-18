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

    /// <summary>Returns at most one byte per <see cref="Read"/> call, forcing every
    /// multi-byte lookahead in the parser through its slowest, most-refilled path.</summary>
    private sealed class OneByteAtATimeStream(byte[] data) : Stream
    {
        private int _pos;
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => data.Length;
        public override long Position { get => _pos; set => throw new NotSupportedException(); }
        public override void Flush() { }

        public override int Read(byte[] buffer, int offset, int count)
        {
            if (_pos >= data.Length || count == 0) { return 0; }
            buffer[offset] = data[_pos++];
            return 1;
        }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }

    // ── Empty SST (no <si> elements) ────────────────────────────────────────

    [Fact]
    public void Parse_SstWithNoSiElements_ReturnsEmptyTable()
    {
        using var stream = Utf8("""<sst uniqueCount="0"></sst>""");
        SharedStringTable result = SharedStringsByteParser.Parse(stream);
        Assert.Equal(0, result.Count);
        Assert.Equal(0, result.CacheLength);
        Assert.Equal(0, result.FirstInfoChunkLength);
    }

    [Fact]
    public void Parse_EmptySst_SelfClosingTag_ReturnsEmptyTable()
    {
        using var stream = Utf8("""<sst/>""");
        SharedStringTable result = SharedStringsByteParser.Parse(stream);
        Assert.Equal(0, result.Count);
        Assert.Equal(131072, result.CacheLength);
    }

    [Fact]
    public void Parse_SstWithDeclaredUniqueCount_SizesCacheAndInfoChunkToWorkbook()
    {
        using var stream = Utf8("""
            <sst uniqueCount="3">
              <si><t>Alpha</t></si>
              <si><t>Beta</t></si>
              <si><t>Gamma</t></si>
            </sst>
            """);
        SharedStringTable result = SharedStringsByteParser.Parse(stream);

        Assert.Equal(3, result.CacheLength);
        Assert.Equal(3, result.FirstInfoChunkLength);
        Assert.Equal(3, result.Count);
        Assert.Equal("Alpha", result.GetString(0));
        Assert.Equal("Gamma", result.GetString(2));
    }

    [Fact]
    public void Parse_MismatchedUniqueCount_GrowsFirstInfoChunkAndParsesAllEntries()
    {
        using var stream = Utf8("""
            <sst uniqueCount="1">
              <si><t>One</t></si>
              <si><t>Two</t></si>
              <si><t>Three</t></si>
            </sst>
            """);
        SharedStringTable result = SharedStringsByteParser.Parse(stream);

        Assert.Equal(3, result.Count);
        Assert.Equal(SharedStringsByteParser.ParseState.InfoChunkSize, result.FirstInfoChunkLength);
        Assert.Equal("One", result.GetString(0));
        Assert.Equal("Three", result.GetString(2));
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

    // ── Self-closing <t/> (empty shared string) ──────────────────────────────

    // Regression: a self-closing <t/> was treated as an opening tag with no closer,
    // so the "skip to closing tag" step after it consumed the following </si> instead,
    // merging this entry with the next and shifting every later index by one.
    [Fact]
    public void Parse_SelfClosingTTag_ProducesEmptyStringWithoutShiftingLaterEntries()
    {
        using var stream = Utf8("""
            <sst>
              <si><t>First</t></si>
              <si><t/></si>
              <si><t>Third</t></si>
            </sst>
            """);
        SharedStringTable result = SharedStringsByteParser.Parse(stream);
        Assert.Equal(3, result.Count);
        Assert.Equal("First", result.GetString(0));
        Assert.Equal("", result.GetString(1));
        Assert.Equal("Third", result.GetString(2));
    }

    // ── Entities split across partial reads ──────────────────────────────────

    // Regression: ResolveEntity only refilled once regardless of how little that
    // refill returned, so a stream serving very few bytes per read could leave the
    // buffer short of the terminating ';' and emit the entity as literal text.
    [Fact]
    public void Parse_NumericEntitiesUnderOneByteAtATimeReads_DecodeCorrectly()
    {
        using var stream = new OneByteAtATimeStream(Encoding.UTF8.GetBytes(
            "<sst><si><t>&#65;&#66;&#x43;</t></si></sst>"));
        SharedStringTable result = SharedStringsByteParser.Parse(stream);
        Assert.Equal(1, result.Count);
        Assert.Equal("ABC", result.GetString(0));
    }

    // ── Namespace-prefixed closing tag under partial reads ───────────────────

    // Regression: HandleClosingTag's lookahead guard assumed a fixed few bytes were
    // enough to judge "</...si>", but a namespace prefix needs more. Under partial
    // reads this made a genuine </x:si> look inconclusive; IsCloseSiTag returned
    // false instead of "not enough data yet", so the tag was treated as ordinary
    // content and the entry merged with the next one.
    [Fact]
    public void Parse_PrefixedSelfClosingTUnderOneByteAtATimeReads_DoesNotMergeEntries()
    {
        using var stream = new OneByteAtATimeStream(Encoding.UTF8.GetBytes(
            """
            <x:sst xmlns:x="http://schemas.openxmlformats.org/spreadsheetml/2006/main">
              <x:si><t/></x:si>
              <x:si><t style="a/b"/></x:si>
              <x:si><t>Third</t></x:si>
            </x:sst>
            """));
        SharedStringTable result = SharedStringsByteParser.Parse(stream);
        Assert.Equal(3, result.Count);
        Assert.Equal("", result.GetString(0));
        Assert.Equal("", result.GetString(1));
        Assert.Equal("Third", result.GetString(2));
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
