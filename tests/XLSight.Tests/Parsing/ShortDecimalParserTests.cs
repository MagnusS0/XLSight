using System.Buffers.Text;
using System.Globalization;
using System.Text;
using XLSight.Internal.Readers.Xlsx;
using Xunit;

namespace XLSight.Tests.Parsing;

/// <summary>
/// The short-decimal fast path must be bit-identical to <see cref="Utf8Parser"/> wherever it
/// accepts input, and must decline everything else so the general parser keeps its semantics.
/// </summary>
public sealed class ShortDecimalParserTests
{
    [Theory]
    [InlineData("0")]
    [InlineData("-0")]
    [InlineData("-0.0")]
    [InlineData("00012")]
    [InlineData("12.5")]
    [InlineData("-7")]
    [InlineData("0.1")]
    [InlineData("0.3")]
    [InlineData("123456789012345")]
    [InlineData("1234567.89012345")]
    [InlineData("0.00000000000001")]
    public void AcceptedInputs_MatchUtf8ParserBitForBit(string text)
    {
        byte[] bytes = Encoding.ASCII.GetBytes(text);

        Assert.True(Utf8CellDecoder.TryParseShortDecimal(bytes, out double fast));
        Assert.True(Utf8Parser.TryParse(bytes, out double expected, out _));
        Assert.Equal(BitConverter.DoubleToInt64Bits(expected), BitConverter.DoubleToInt64Bits(fast));
    }

    [Theory]
    [InlineData("")]
    [InlineData("-")]
    [InlineData(".")]
    [InlineData(".5")]
    [InlineData("1.")]
    [InlineData("1e5")]
    [InlineData("+1")]
    [InlineData("1.2.3")]
    [InlineData("12abc")]
    [InlineData("1234567890123456")]
    [InlineData("9007199254740993")]
    [InlineData("0.123456789012345")]
    public void UnusualInputs_AreDeclined(string text)
    {
        Assert.False(Utf8CellDecoder.TryParseShortDecimal(Encoding.ASCII.GetBytes(text), out _));
    }

    [Fact]
    public void RandomDecimals_MatchUtf8ParserBitForBit()
    {
        var random = new Random(20261026);
        int accepted = 0;
        for (int i = 0; i < 200_000; i++)
        {
            int intDigits = random.Next(1, 12);
            int fracDigits = random.Next(0, 16 - intDigits);
            string text = (random.Next(4) == 0 ? "-" : "")
                + random.NextInt64((long)Math.Pow(10, intDigits - 1), (long)Math.Pow(10, intDigits)).ToString(CultureInfo.InvariantCulture)
                + (fracDigits == 0 ? "" : "." + random.NextInt64(0, (long)Math.Pow(10, fracDigits)).ToString($"D{fracDigits}", CultureInfo.InvariantCulture));
            byte[] bytes = Encoding.ASCII.GetBytes(text);

            Assert.True(Utf8Parser.TryParse(bytes, out double expected, out _));
            if (Utf8CellDecoder.TryParseShortDecimal(bytes, out double fast))
            {
                accepted++;
                Assert.True(
                    BitConverter.DoubleToInt64Bits(expected) == BitConverter.DoubleToInt64Bits(fast),
                    $"'{text}': fast {fast:R} != Utf8Parser {expected:R}");
            }
        }

        Assert.True(accepted > 150_000, $"fast path accepted only {accepted} of 200000 short decimals");
    }
}
