using Xunit;
using XLSight;
using XLSight.Exceptions;
using XLSight.Models;
using XLSight.Internal.Parsing;

namespace XLSight.Tests.Parsing;

public sealed class CellReferenceParserTests
{
    [Theory]
    [InlineData("A1", 1, 1)]
    [InlineData("BC123", 55, 123)]
    [InlineData("XFD1048576", 16384, 1048576)]
    public void Parse_ValidReference_ReturnsExpectedAddress(string input, int expectedColumn, int expectedRow)
    {
        ExcelAddress actual = CellReferenceParser.Parse(input);

        Assert.Equal(new ExcelAddress(expectedColumn, expectedRow), actual);
    }

    [Theory]
    [InlineData("")]
    [InlineData("A")]
    [InlineData("123")]
    [InlineData("A0")]
    [InlineData("XFE1")]
    [InlineData("BC1048577")]
    [InlineData("bc123")]
    public void Parse_InvalidReference_ThrowsInvalidAddressException(string input)
    {
        var exception = Assert.Throws<InvalidAddressException>(() => CellReferenceParser.Parse(input));
        Assert.Equal(input, exception.Address);
    }

    [Fact]
    public void TryParse_InvalidReference_ReturnsFalseAndDefaultAddress()
    {
        bool success = CellReferenceParser.TryParse("A-1", out ExcelAddress actual);

        Assert.False(success);
        Assert.Equal(default, actual);
    }
}
