using Xunit;
using XLSight.Models;

namespace XLSight.Tests.Models;

public sealed class ExcelCellValueTests
{
    // ── Empty sentinel ────────────────────────────────────────────────────────

    [Fact]
    public void Empty_HasCellTypeEmpty()
    {
        Assert.Equal(CellType.Empty, ExcelCellValue.Empty.CellType);
    }

    [Fact]
    public void Empty_IsEmpty_IsTrue()
    {
        Assert.True(ExcelCellValue.Empty.IsEmpty);
    }

    [Fact]
    public void Empty_HasValue_IsFalse()
    {
        Assert.False(ExcelCellValue.Empty.HasValue);
    }

    [Fact]
    public void Empty_ToString_ReturnsNonNullNonEmpty()
    {
        var s = ExcelCellValue.Empty.ToString();
        Assert.NotNull(s);
        Assert.NotEmpty(s);
    }

    // ── Factory methods produce correct CellType ──────────────────────────────

    [Fact]
    public void FromNumber_HasCellTypeNumber() =>
        Assert.Equal(CellType.Number, ExcelCellValue.FromNumber(42.0).CellType);

    [Fact]
    public void FromDate_HasCellTypeDate() =>
        Assert.Equal(CellType.Date, ExcelCellValue.FromDate(DateTime.Today).CellType);

    [Fact]
    public void FromText_HasCellTypeText() =>
        Assert.Equal(CellType.Text, ExcelCellValue.FromText("hello").CellType);

    [Fact]
    public void FromBoolean_HasCellTypeBoolean() =>
        Assert.Equal(CellType.Boolean, ExcelCellValue.FromBoolean(true).CellType);

    [Fact]
    public void FromError_HasCellTypeError() =>
        Assert.Equal(CellType.Error, ExcelCellValue.FromError("#REF!").CellType);

    [Fact]
    public void FromFormula_HasCellTypeFormula() =>
        Assert.Equal(CellType.Formula, ExcelCellValue.FromFormula("SUM(A1:A10)").CellType);

    // ── Typed accessors — correct values ──────────────────────────────────────

    [Theory]
    [InlineData(0.0)]
    [InlineData(1.5)]
    [InlineData(-99.9)]
    [InlineData(double.MaxValue)]
    public void AsNumber_ReturnsStoredValue(double input) =>
        Assert.Equal(input, ExcelCellValue.FromNumber(input).AsNumber());

    [Fact]
    public void AsDate_RoundTrips()
    {
        var dt = new DateTime(2024, 6, 15, 10, 30, 0, DateTimeKind.Unspecified);
        Assert.Equal(dt, ExcelCellValue.FromDate(dt).AsDate());
    }

    [Theory]
    [InlineData("hello")]
    [InlineData("")]
    public void AsText_ReturnsStoredString(string input) =>
        Assert.Equal(input, ExcelCellValue.FromText(input).AsText());

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void AsBoolean_ReturnsStoredValue(bool input) =>
        Assert.Equal(input, ExcelCellValue.FromBoolean(input).AsBoolean());

    [Fact]
    public void AsError_ReturnsStoredCode() =>
        Assert.Equal("#REF!", ExcelCellValue.FromError("#REF!").AsError());

    [Fact]
    public void AsFormula_ReturnsStoredText() =>
        Assert.Equal("SUM(A1:A10)", ExcelCellValue.FromFormula("SUM(A1:A10)").AsFormula());

    // ── Wrong-type accessors throw ────────────────────────────────────────────

    [Fact]
    public void AsNumber_OnTextCell_Throws() =>
        Assert.Throws<InvalidOperationException>(() => ExcelCellValue.FromText("x").AsNumber());

    [Fact]
    public void AsDate_OnNumberCell_Throws() =>
        Assert.Throws<InvalidOperationException>(() => ExcelCellValue.FromNumber(1.0).AsDate());

    [Fact]
    public void AsText_OnNumberCell_Throws() =>
        Assert.Throws<InvalidOperationException>(() => ExcelCellValue.FromNumber(1.0).AsText());

    [Fact]
    public void AsBoolean_OnTextCell_Throws() =>
        Assert.Throws<InvalidOperationException>(() => ExcelCellValue.FromText("true").AsBoolean());

    [Fact]
    public void AsNumber_OnEmptyCell_Throws() =>
        Assert.Throws<InvalidOperationException>(() => ExcelCellValue.Empty.AsNumber());

    // ── Try-pattern accessors ─────────────────────────────────────────────────

    [Fact]
    public void TryGetNumber_OnNumberCell_ReturnsTrueAndValue()
    {
        Assert.True(ExcelCellValue.FromNumber(3.14).TryGetNumber(out double v));
        Assert.Equal(3.14, v);
    }

    [Fact]
    public void TryGetNumber_OnTextCell_ReturnsFalse()
    {
        Assert.False(ExcelCellValue.FromText("x").TryGetNumber(out double v));
        Assert.Equal(0.0, v);
    }

    [Fact]
    public void TryGetDate_OnDateCell_ReturnsTrueAndValue()
    {
        var dt = new DateTime(2025, 1, 1);
        Assert.True(ExcelCellValue.FromDate(dt).TryGetDate(out DateTime v));
        Assert.Equal(dt, v);
    }

    [Fact]
    public void TryGetText_OnTextCell_ReturnsTrueAndValue()
    {
        Assert.True(ExcelCellValue.FromText("hello").TryGetText(out string? v));
        Assert.Equal("hello", v);
    }

    [Fact]
    public void TryGetText_OnNumberCell_ReturnsFalseAndNull()
    {
        Assert.False(ExcelCellValue.FromNumber(1.0).TryGetText(out string? v));
        Assert.Null(v);
    }

    [Fact]
    public void TryGetBoolean_OnBoolCell_ReturnsTrueAndValue()
    {
        Assert.True(ExcelCellValue.FromBoolean(false).TryGetBoolean(out bool v));
        Assert.False(v);
    }

    // ── Equality ──────────────────────────────────────────────────────────────

    [Fact]
    public void Equality_SameNumber_AreEqual()
    {
        var a = ExcelCellValue.FromNumber(42.0);
        var b = ExcelCellValue.FromNumber(42.0);
        Assert.Equal(a, b);
        Assert.True(a == b);
        Assert.False(a != b);
    }

    [Fact]
    public void Equality_DifferentNumbers_AreNotEqual()
    {
        Assert.NotEqual(ExcelCellValue.FromNumber(1.0), ExcelCellValue.FromNumber(2.0));
    }

    [Fact]
    public void Equality_SameValueDifferentType_AreNotEqual()
    {
        // Number 1.0 vs Boolean true — same _numeric but different _type
        Assert.NotEqual(ExcelCellValue.FromNumber(1.0), ExcelCellValue.FromBoolean(true));
    }

    [Fact]
    public void Equality_EmptyEqualsDefault()
    {
        Assert.Equal(ExcelCellValue.Empty, default(ExcelCellValue));
        Assert.True(ExcelCellValue.Empty == default);
    }

    [Fact]
    public void GetHashCode_EqualValues_SameHash()
    {
        var a = ExcelCellValue.FromText("abc");
        var b = ExcelCellValue.FromText("abc");
        Assert.Equal(a.GetHashCode(), b.GetHashCode());
    }

    // ── ToString ──────────────────────────────────────────────────────────────

    [Fact]
    public void ToString_Date_FormatsAsIsoDate() =>
        Assert.Equal("2024-03-15", ExcelCellValue.FromDate(new DateTime(2024, 3, 15)).ToString());

    [Fact]
    public void ToString_BoolTrue_ReturnsTRUE() =>
        Assert.Equal("TRUE", ExcelCellValue.FromBoolean(true).ToString());

    [Fact]
    public void ToString_BoolFalse_ReturnsFALSE() =>
        Assert.Equal("FALSE", ExcelCellValue.FromBoolean(false).ToString());

    [Fact]
    public void ToString_Text_ReturnsText() =>
        Assert.Equal("hello", ExcelCellValue.FromText("hello").ToString());

    [Fact]
    public void ToString_AllTypes_NonNull()
    {
        Assert.NotNull(ExcelCellValue.Empty.ToString());
        Assert.NotNull(ExcelCellValue.FromNumber(1.5).ToString());
        Assert.NotNull(ExcelCellValue.FromDate(DateTime.Today).ToString());
        Assert.NotNull(ExcelCellValue.FromError("#REF!").ToString());
        Assert.NotNull(ExcelCellValue.FromFormula("SUM()").ToString());
    }
}
