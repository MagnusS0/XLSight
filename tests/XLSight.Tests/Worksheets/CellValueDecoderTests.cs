using Xunit;
using XLSight.Models;
using XLSight.Styles;
using XLSight.Worksheets;

namespace XLSight.Tests.Worksheets;

public sealed class CellValueDecoderTests
{
    private static readonly string[] TwoStrings = ["zero", "one"];
    private static readonly string[] EmptyStrings = [];

    private static ParsedCell Cell(
        CellDataKind kind,
        string rawValue = "",
        string? inlineStr = null,
        string? formula = null,
        int styleIndex = 0,
        int row = 1, int col = 1)
        => new ParsedCell(row, col, styleIndex, kind, rawValue.AsMemory(), inlineStr, formula);

    [Fact]
    public void Decode_SharedString_ReturnsCorrectText()
    {
        var result = CellValueDecoder.Decode(Cell(CellDataKind.SharedString, "1"), TwoStrings, StyleTable.Default, false, ExcelReadMode.Values);
        Assert.Equal(ExcelCellValue.FromText("one"), result);
    }

    [Fact]
    public void Decode_SharedString_EmptyRawValue_ReturnsEmpty()
    {
        var result = CellValueDecoder.Decode(Cell(CellDataKind.SharedString, ""), TwoStrings, StyleTable.Default, false, ExcelReadMode.Values);
        Assert.Equal(ExcelCellValue.Empty, result);
    }

    [Fact]
    public void Decode_SharedString_OutOfRangeIndex_ReturnsEmpty()
    {
        var result = CellValueDecoder.Decode(Cell(CellDataKind.SharedString, "99"), ["only"], StyleTable.Default, false, ExcelReadMode.Values);
        Assert.Equal(ExcelCellValue.Empty, result);
    }

    [Fact]
    public void Decode_SharedString_InvalidIndex_ReturnsEmpty()
    {
        var result = CellValueDecoder.Decode(Cell(CellDataKind.SharedString, "abc"), TwoStrings, StyleTable.Default, false, ExcelReadMode.Values);
        Assert.Equal(ExcelCellValue.Empty, result);
    }

    [Fact]
    public void Decode_Boolean_True_ReturnsTrue()
    {
        var result = CellValueDecoder.Decode(Cell(CellDataKind.Boolean, "1"), EmptyStrings, StyleTable.Default, false, ExcelReadMode.Values);
        Assert.Equal(ExcelCellValue.FromBoolean(true), result);
    }

    [Fact]
    public void Decode_Boolean_False_ReturnsFalse()
    {
        var result = CellValueDecoder.Decode(Cell(CellDataKind.Boolean, "0"), EmptyStrings, StyleTable.Default, false, ExcelReadMode.Values);
        Assert.Equal(ExcelCellValue.FromBoolean(false), result);
    }

    [Fact]
    public void Decode_Boolean_Empty_ReturnsEmpty()
    {
        var result = CellValueDecoder.Decode(Cell(CellDataKind.Boolean, ""), EmptyStrings, StyleTable.Default, false, ExcelReadMode.Values);
        Assert.Equal(ExcelCellValue.Empty, result);
    }

    [Fact]
    public void Decode_InlineString_ReturnsText()
    {
        var result = CellValueDecoder.Decode(Cell(CellDataKind.InlineString, inlineStr: "Hello"), EmptyStrings, StyleTable.Default, false, ExcelReadMode.Values);
        Assert.Equal(ExcelCellValue.FromText("Hello"), result);
    }

    [Fact]
    public void Decode_InlineString_NullInlineString_ReturnsEmpty()
    {
        var result = CellValueDecoder.Decode(Cell(CellDataKind.InlineString), EmptyStrings, StyleTable.Default, false, ExcelReadMode.Values);
        Assert.Equal(ExcelCellValue.Empty, result);
    }

    [Fact]
    public void Decode_Error_ReturnsErrorCode()
    {
        var result = CellValueDecoder.Decode(Cell(CellDataKind.Error, "#DIV/0!"), EmptyStrings, StyleTable.Default, false, ExcelReadMode.Values);
        Assert.Equal(ExcelCellValue.FromError("#DIV/0!"), result);
    }

    [Fact]
    public void Decode_FormulaString_ReturnsText()
    {
        var result = CellValueDecoder.Decode(Cell(CellDataKind.FormulaString, "result"), EmptyStrings, StyleTable.Default, false, ExcelReadMode.Values);
        Assert.Equal(ExcelCellValue.FromText("result"), result);
    }

    [Fact]
    public void Decode_Number_ReturnsDouble()
    {
        var result = CellValueDecoder.Decode(Cell(CellDataKind.Number, "3.14"), EmptyStrings, StyleTable.Default, false, ExcelReadMode.Values);
        Assert.Equal(ExcelCellValue.FromNumber(3.14), result);
    }

    [Fact]
    public void Decode_Number_EmptyValue_ReturnsEmpty()
    {
        var result = CellValueDecoder.Decode(Cell(CellDataKind.Number, ""), EmptyStrings, StyleTable.Default, false, ExcelReadMode.Values);
        Assert.Equal(ExcelCellValue.Empty, result);
    }

    [Fact]
    public void Decode_Number_WithDateStyle_ReturnsDate()
    {
        var styleTable = new StyleTable([FormatClass.Date]);
        var result = CellValueDecoder.Decode(Cell(CellDataKind.Number, "44927", styleIndex: 0), EmptyStrings, styleTable, false, ExcelReadMode.Values);
        Assert.Equal(ExcelCellType.Date, result.CellType);
        Assert.Equal(new DateTime(2023, 1, 1), result.AsDate());
    }

    [Fact]
    public void Decode_Number_WithGeneralStyle_ReturnsNumber()
    {
        var result = CellValueDecoder.Decode(Cell(CellDataKind.Number, "44927", styleIndex: 0), EmptyStrings, StyleTable.Default, false, ExcelReadMode.Values);
        Assert.Equal(ExcelCellValue.FromNumber(44927.0), result);
    }

    [Fact]
    public void Decode_FormulasMode_HasFormula_ReturnsFormula()
    {
        var result = CellValueDecoder.Decode(Cell(CellDataKind.Number, "42", formula: "SUM(A1:B1)"), EmptyStrings, StyleTable.Default, false, ExcelReadMode.Formulas);
        Assert.Equal(ExcelCellValue.FromFormula("SUM(A1:B1)"), result);
    }

    [Fact]
    public void Decode_FormulasMode_NoFormula_ReturnsNormalValue()
    {
        var result = CellValueDecoder.Decode(Cell(CellDataKind.Number, "42"), EmptyStrings, StyleTable.Default, false, ExcelReadMode.Formulas);
        Assert.Equal(ExcelCellValue.FromNumber(42.0), result);
    }

    [Fact]
    public void Decode_PhantomLeapDay_Serial60_ReturnsNumber()
    {
        var styleTable = new StyleTable([FormatClass.Date]);
        var result = CellValueDecoder.Decode(Cell(CellDataKind.Number, "60", styleIndex: 0), EmptyStrings, styleTable, false, ExcelReadMode.Values);
        Assert.Equal(ExcelCellValue.FromNumber(60.0), result);
    }
}
