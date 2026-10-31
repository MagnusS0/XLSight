using XLSight.Internal.Parsing;
using Xunit;

namespace XLSight.Tests.Parsing;

public sealed class ExcelTextDecoderTests
{
    [Theory]
    [InlineData("Plain text")]
    [InlineData("under_score and _x12G4_ and _x123_")]
    public void Decode_WithoutValidEscapes_ReturnsOriginalString(string text)
    {
        Assert.Same(text, ExcelTextDecoder.Decode(text));
    }
}
