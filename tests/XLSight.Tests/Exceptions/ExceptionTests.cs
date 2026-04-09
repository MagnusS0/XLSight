using XLSight.Exceptions;
using Xunit;

namespace XLSight.Tests.Exceptions;

public sealed class ExceptionTests
{
    // ── RangeTooLargeException (0% coverage) ─────────────────────────────────

    [Fact]
    public void RangeTooLarge_Constructor_SetsProperties()
    {
        var ex = new RangeTooLargeException(200_000_000L, 100_000_000L);

        Assert.Equal(200_000_000L, ex.RequestedCells);
        Assert.Equal(100_000_000L, ex.MaxCells);
        Assert.NotNull(ex.Message);
        Assert.Contains("200", ex.Message, StringComparison.Ordinal);
        Assert.Contains("100", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void RangeTooLarge_IsExcelException()
    {
        var ex = new RangeTooLargeException(1L, 0L);
        Assert.IsAssignableFrom<ExcelException>(ex);
    }

    // ── ExcelException inner-exception overload ───────────────────────────────

    [Fact]
    public void ExcelException_WithInnerException_PreservesInner()
    {
        var inner = new InvalidOperationException("inner");
        var ex = new ExcelException("outer", inner);

        Assert.Equal("outer", ex.Message);
        Assert.Same(inner, ex.InnerException);
    }

    // ── InvalidAddressException ───────────────────────────────────────────────

    [Fact]
    public void InvalidAddress_WithAddress_SetsProperty()
    {
        var ex = new InvalidAddressException("BAD_ADDR");

        Assert.Equal("BAD_ADDR", ex.Address);
        Assert.Contains("BAD_ADDR", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void InvalidAddress_WithReason_IncludesReason()
    {
        var ex = new InvalidAddressException("BAD_ADDR", "column too large");

        Assert.Equal("BAD_ADDR", ex.Address);
        Assert.Contains("column too large", ex.Message, StringComparison.Ordinal);
    }

    // ── MalformedWorkbookException ────────────────────────────────────────────

    [Fact]
    public void MalformedWorkbook_WithInnerException_PreservesInner()
    {
        var inner = new FormatException("xml bad");
        var ex = new MalformedWorkbookException("bad workbook", inner);

        Assert.Equal("bad workbook", ex.Message);
        Assert.Same(inner, ex.InnerException);
    }

    // ── SheetNotFoundException ────────────────────────────────────────────────

    [Fact]
    public void SheetNotFound_SetsSheetName()
    {
        var ex = new SheetNotFoundException("MySheet");

        Assert.Equal("MySheet", ex.SheetName);
        Assert.Contains("MySheet", ex.Message, StringComparison.Ordinal);
    }
}
