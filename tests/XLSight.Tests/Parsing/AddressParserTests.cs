using XLSight.Exceptions;
using XLSight.Internal.Parsing;
using XLSight.Models;
using Xunit;

namespace XLSight.Tests.Parsing;

public sealed class AddressParserTests
{
    [Theory]
    [InlineData("A1", 1, 1, 1, 1)]
    [InlineData("Z99", 26, 99, 26, 99)]
    [InlineData("AA1", 27, 1, 27, 1)]
    [InlineData("XFD1048576", 16384, 1048576, 16384, 1048576)]
    [InlineData("A1:C50", 1, 1, 3, 50)]
    [InlineData("A:C", 1, 1, 3, ExcelLimits.MaxRows)]
    [InlineData("1:10", 1, 1, ExcelLimits.MaxColumns, 10)]
    public void Parse_ValidAddress_ReturnsExpectedRange(
        string input,
        int expectedStartColumn,
        int expectedStartRow,
        int expectedEndColumn,
        int expectedEndRow)
    {
        ExcelRange actual = AddressParser.Parse(input);

        Assert.False(actual.IsUnbounded);
        Assert.Equal(new ExcelAddress(expectedStartColumn, expectedStartRow), actual.TopLeft);
        Assert.Equal(new ExcelAddress(expectedEndColumn, expectedEndRow), actual.BottomRight);
    }

    [Fact]
    public void Parse_FullSheet_ReturnsUnbounded()
    {
        ExcelRange actual = AddressParser.Parse(":");

        Assert.Equal(ExcelRange.Unbounded, actual);
        Assert.True(actual.IsUnbounded);
    }

    [Theory]
    [InlineData("")]
    [InlineData("A")]
    [InlineData("1")]
    [InlineData("A0")]
    [InlineData("XFE1")]
    [InlineData("A1048577")]
    [InlineData("0:10")]
    [InlineData("A:XFE")]
    [InlineData("B1:A1")]
    [InlineData("A1:B0")]
    [InlineData("A1:")]
    [InlineData(":A1")]
    [InlineData("A1:B2:C3")]
    [InlineData("a1")]
    public void Parse_InvalidAddress_ThrowsInvalidAddressException(string input)
    {
        var exception = Assert.Throws<InvalidAddressException>(() => AddressParser.Parse(input));
        Assert.Equal(input, exception.Address);
    }

    [Theory]
    [InlineData("A", 1)]
    [InlineData("Z", 26)]
    [InlineData("AA", 27)]
    [InlineData("XFD", 16384)]
    public void TryParseColumn_ValidLetters_ReturnsExpectedIndex(string input, int expectedColumn)
    {
        bool success = AddressParser.TryParseColumn(input, out int actualColumn);

        Assert.True(success);
        Assert.Equal(expectedColumn, actualColumn);
    }

    [Theory]
    [InlineData("")]
    [InlineData("AAAA")]
    [InlineData("XFE")]
    [InlineData("A1")]
    [InlineData("a")]
    public void TryParseColumn_InvalidLetters_ReturnsFalse(string input)
    {
        bool success = AddressParser.TryParseColumn(input, out int actualColumn);

        Assert.False(success);
        Assert.Equal(0, actualColumn);
    }
}
