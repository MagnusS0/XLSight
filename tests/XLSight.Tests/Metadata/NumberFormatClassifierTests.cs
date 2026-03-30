using XLSight.Internal.Metadata;
using Xunit;

namespace XLSight.Tests.Metadata;

public sealed class NumberFormatClassifierTests
{
    // ── Built-in format IDs ───────────────────────────────────────────────────

    [Theory]
    [InlineData(0, 0)]   // General
    [InlineData(1, 1)]   // Number
    [InlineData(14, 2)]  // Date
    [InlineData(15, 2)]  // Date
    [InlineData(18, 3)]  // Time
    [InlineData(19, 3)]  // Time
    [InlineData(22, 4)]  // DateTime
    [InlineData(45, 3)]  // Time
    [InlineData(46, 3)]  // Time
    [InlineData(47, 3)]  // Time
    [InlineData(49, 5)]  // Text
    public void Classify_BuiltInId_ReturnsExpected(int numFmtId, int expected)
    {
        FormatClass actual = NumberFormatClassifier.Classify(numFmtId, null);

        Assert.Equal((FormatClass)expected, actual);
    }

    [Fact]
    public void Classify_UnknownBuiltInId_ReturnsGeneral()
    {
        // ID 100 is not in the built-in table and is below 164 → General.
        FormatClass actual = NumberFormatClassifier.Classify(100, null);

        Assert.Equal(FormatClass.General, actual);
    }

    // ── Custom formats (numFmtId = 164) ──────────────────────────────────────

    [Theory]
    [InlineData("yyyy-mm-dd", 2)]   // Date
    [InlineData("dd/mm/yyyy", 2)]   // Date
    [InlineData("hh:mm:ss", 3)]     // Time
    [InlineData("h:mm AM/PM", 3)]   // Time
    [InlineData("m/d/yyyy h:mm", 4)] // DateTime
    [InlineData("#,##0", 1)]         // Number
    [InlineData("#,##0.00", 1)]      // Number
    [InlineData("0.00%", 1)]         // Number
    [InlineData("@", 1)]             // '@' is not a date/time token → Number
    public void Classify_CustomFormat_ReturnsExpected(string formatCode, int expected)
    {
        FormatClass actual = NumberFormatClassifier.Classify(164, formatCode);

        Assert.Equal((FormatClass)expected, actual);
    }

    // ── 'm' ambiguity ────────────────────────────────────────────────────────

    [Theory]
    [InlineData("mm:ss", 3)]              // m before s  → minute → Time
    [InlineData("hh:mm", 3)]              // m after h   → minute → Time
    [InlineData("dd/mm/yyyy", 2)]         // m between d and y → month → Date
    [InlineData("yyyy/mm/dd", 2)]         // m after y, before d → month → Date
    [InlineData("mm/dd/yyyy hh:mm:ss", 4)] // first m=month, second m=minute → DateTime
    public void Classify_MAmbiguity_ResolvedCorrectly(string formatCode, int expected)
    {
        FormatClass actual = NumberFormatClassifier.Classify(164, formatCode);

        Assert.Equal((FormatClass)expected, actual);
    }

    // ── Literal stripping ────────────────────────────────────────────────────

    [Theory]
    [InlineData("\"Date: \"yyyy-mm-dd", 2)]  // quoted prefix is stripped → Date
    [InlineData("[Red]yyyy-mm-dd", 2)]        // color code is stripped → Date
    public void Classify_StripsLiteralsAndColorCodes(string formatCode, int expected)
    {
        FormatClass actual = NumberFormatClassifier.Classify(164, formatCode);

        Assert.Equal((FormatClass)expected, actual);
    }
}
