using XLSight.Internal.Metadata;
using Xunit;

namespace XLSight.Tests.Metadata;

public sealed class ExcelDateConverterTests
{
    // ── 1900 date system (isDate1904 = false) ──────────────────────────────

    [Fact]
    public void FromSerial_NegativeSerial_ReturnsNull()
    {
        Assert.Null(ExcelDateConverter.FromSerial(-1, isDate1904: false));
    }

    [Fact]
    public void FromSerial_SerialZero_ReturnsDateTimeMinValue()
    {
        Assert.Equal(DateTime.MinValue, ExcelDateConverter.FromSerial(0, isDate1904: false));
    }

    [Fact]
    public void FromSerial_SerialZeroPointFive_ReturnsNoonOnDayZero()
    {
        var expected = DateTime.MinValue.AddDays(0.5);
        Assert.Equal(expected, ExcelDateConverter.FromSerial(0.5, isDate1904: false));
    }

    [Fact]
    public void FromSerial_Serial1_ReturnsJan1_1900()
    {
        Assert.Equal(new DateTime(1900, 1, 1), ExcelDateConverter.FromSerial(1, isDate1904: false));
    }

    [Fact]
    public void FromSerial_Serial59_ReturnsFeb28_1900()
    {
        Assert.Equal(new DateTime(1900, 2, 28), ExcelDateConverter.FromSerial(59, isDate1904: false));
    }

    [Theory]
    [InlineData(60.0)]
    [InlineData(60.5)]
    public void FromSerial_PhantomFeb29_1900_ReturnsNull(double serial)
    {
        Assert.Null(ExcelDateConverter.FromSerial(serial, isDate1904: false));
    }

    [Fact]
    public void FromSerial_Serial61_ReturnsMar1_1900()
    {
        Assert.Equal(new DateTime(1900, 3, 1), ExcelDateConverter.FromSerial(61, isDate1904: false));
    }

    [Fact]
    public void FromSerial_Serial44927_ReturnsJan1_2023()
    {
        Assert.Equal(new DateTime(2023, 1, 1), ExcelDateConverter.FromSerial(44927, isDate1904: false));
    }

    [Fact]
    public void FromSerial_Serial44927Point5_ReturnsNoonJan1_2023()
    {
        Assert.Equal(new DateTime(2023, 1, 1, 12, 0, 0), ExcelDateConverter.FromSerial(44927.5, isDate1904: false));
    }

    // ── Millisecond carry regression (calamine #602) ──────────────────────
    // A fractional part where (frac * 86400000) ≈ 86399999.9 ms (just under 24h).
    // AddDays handles the carry naturally — verify no exception and a valid DateTime.

    [Fact]
    public void FromSerial_NearBoundaryFraction_DoesNotThrow()
    {
        // 0.9999999884259259 * 86400000 ≈ 86399999.0 ms — very close to 24h boundary
        const double nearBoundaryFraction = 0.9999999884259259;
        var serial = 44927 + nearBoundaryFraction;

        var result = ExcelDateConverter.FromSerial(serial, isDate1904: false);

        Assert.NotNull(result);
        Assert.True(result.Value >= new DateTime(2023, 1, 1));
        Assert.True(result.Value < new DateTime(2023, 1, 2));
    }

    // ── 1904 date system (isDate1904 = true) ──────────────────────────────

    [Fact]
    public void FromSerial_1904_Serial0_ReturnsJan1_1904()
    {
        Assert.Equal(new DateTime(1904, 1, 1), ExcelDateConverter.FromSerial(0, isDate1904: true));
    }

    [Fact]
    public void FromSerial_1904_Serial1_ReturnsJan2_1904()
    {
        Assert.Equal(new DateTime(1904, 1, 2), ExcelDateConverter.FromSerial(1, isDate1904: true));
    }

    [Fact]
    public void FromSerial_1904_NegativeSerial_ReturnsNull()
    {
        Assert.Null(ExcelDateConverter.FromSerial(-1, isDate1904: true));
    }

    [Fact]
    public void FromSerial_1904_Serial365_MatchesComputedExpected()
    {
        // 1904 is a leap year (366 days), so serial 365 = Dec 31, 1904
        var expected = new DateTime(1904, 1, 1).AddDays(365);
        Assert.Equal(expected, ExcelDateConverter.FromSerial(365, isDate1904: true));
    }
}
