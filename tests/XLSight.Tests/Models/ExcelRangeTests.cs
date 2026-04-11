using Xunit;

namespace XLSight.Tests.Models;

public sealed class ExcelRangeTests
{
    // ── ExcelAddress.ToString column-letter conversion ──────────────────────

    [Theory]
    [InlineData(1, "A")]
    [InlineData(26, "Z")]
    [InlineData(27, "AA")]
    [InlineData(703, "AAA")]
    [InlineData(16384, "XFD")]
    public void Address_ToString_ProducesCorrectColumnLetters(int column, string expectedLetters)
    {
        var address = new ExcelAddress(column, 1);
        Assert.Equal($"{expectedLetters}1", address.ToString());
    }

    [Fact]
    public void Address_ToString_IncludesRowNumber()
    {
        var address = new ExcelAddress(3, 42); // C42
        Assert.Equal("C42", address.ToString());
    }

    // ── ExcelAddress record equality ─────────────────────────────────────────

    [Fact]
    public void Address_RecordEquality_SameColumnAndRow_AreEqual()
    {
        var a = new ExcelAddress(5, 10);
        var b = new ExcelAddress(5, 10);
        Assert.Equal(a, b);
        Assert.True(a == b);
    }

    [Fact]
    public void Address_RecordEquality_DifferentColumn_AreNotEqual()
    {
        var a = new ExcelAddress(1, 1);
        var b = new ExcelAddress(2, 1);
        Assert.NotEqual(a, b);
    }

    [Fact]
    public void Address_RecordEquality_DifferentRow_AreNotEqual()
    {
        var a = new ExcelAddress(1, 1);
        var b = new ExcelAddress(1, 2);
        Assert.NotEqual(a, b);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(16385)]
    public void Address_InvalidColumn_ThrowsArgumentOutOfRangeException(int column)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new ExcelAddress(column, 1));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1048577)]
    public void Address_InvalidRow_ThrowsArgumentOutOfRangeException(int row)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new ExcelAddress(1, row));
    }

    // ── ExcelRange bounded Width and Height ──────────────────────────────────

    [Fact]
    public void Range_BoundedA1ToC5_HasWidth3AndHeight5()
    {
        var range = new ExcelRange(new ExcelAddress(1, 1), new ExcelAddress(3, 5));
        Assert.Equal(3, range.Width);
        Assert.Equal(5, range.Height);
    }

    [Fact]
    public void Range_SingleCell_HasWidth1AndHeight1()
    {
        var range = new ExcelRange(new ExcelAddress(7, 7), new ExcelAddress(7, 7));
        Assert.Equal(1, range.Width);
        Assert.Equal(1, range.Height);
    }

    // ── ExcelRange.Unbounded ─────────────────────────────────────────────────

    [Fact]
    public void Range_Unbounded_IsUnbounded_IsTrue()
    {
        Assert.True(ExcelRange.Unbounded.IsUnbounded);
    }

    [Fact]
    public void Range_Bounded_IsUnbounded_IsFalse()
    {
        var range = new ExcelRange(new ExcelAddress(1, 1), new ExcelAddress(3, 5));
        Assert.False(range.IsUnbounded);
    }

    [Fact]
    public void Range_Unbounded_Height_ThrowsInvalidOperationException()
    {
        var ex = Assert.Throws<InvalidOperationException>(() => _ = ExcelRange.Unbounded.Height);
        Assert.Contains("unbounded", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Range_Unbounded_EqualsSelf()
    {
        var a = ExcelRange.Unbounded;
        var b = ExcelRange.Unbounded;
        Assert.Equal(a, b);
        Assert.True(a == b);
    }

    // ── ExcelRange.Contains ──────────────────────────────────────────────────

    [Fact]
    public void Range_Unbounded_ContainsAnyAddress_ReturnsTrue()
    {
        Assert.True(ExcelRange.Unbounded.Contains(new ExcelAddress(1, 1)));
        Assert.True(ExcelRange.Unbounded.Contains(new ExcelAddress(16384, 1048576)));
        Assert.True(ExcelRange.Unbounded.Contains(new ExcelAddress(500, 300)));
    }

    [Fact]
    public void Range_Bounded_Contains_InsideCell_ReturnsTrue()
    {
        // B2:D4 → cols 2-4, rows 2-4
        var range = new ExcelRange(new ExcelAddress(2, 2), new ExcelAddress(4, 4));
        Assert.True(range.Contains(new ExcelAddress(2, 2))); // top-left corner
        Assert.True(range.Contains(new ExcelAddress(4, 4))); // bottom-right corner
        Assert.True(range.Contains(new ExcelAddress(3, 3))); // center
    }

    [Fact]
    public void Range_Bounded_Contains_OutsideCell_ReturnsFalse()
    {
        // B2:D4 → cols 2-4, rows 2-4
        var range = new ExcelRange(new ExcelAddress(2, 2), new ExcelAddress(4, 4));
        Assert.False(range.Contains(new ExcelAddress(1, 3))); // column too small
        Assert.False(range.Contains(new ExcelAddress(5, 3))); // column too large
        Assert.False(range.Contains(new ExcelAddress(3, 1))); // row too small
        Assert.False(range.Contains(new ExcelAddress(3, 5))); // row too large
    }
}
