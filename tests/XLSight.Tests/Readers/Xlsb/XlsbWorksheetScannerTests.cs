using System.Runtime.InteropServices;
using XLSight.Internal.Metadata;
using XLSight.Internal.Readers.Xlsb;
using XLSight.Internal.Sinks;
using XLSight.Analysis;
using Xunit;

namespace XLSight.Tests.Readers.Xlsb;

public sealed class XlsbWorksheetScannerTests
{
    [Fact]
    public void ScanRows_DecodesSupportedCellRecords()
    {
        using var stream = XlsbTestRecords.Stream(
            XlsbTestRecords.Row(1),
            XlsbTestRecords.Blank(1),
            XlsbTestRecords.RkInt(2, 42),
            XlsbTestRecords.Real(3, 3.25),
            XlsbTestRecords.Bool(4, true),
            XlsbTestRecords.Error(5, 0x17),
            XlsbTestRecords.InlineString(6, "inline"),
            XlsbTestRecords.SharedString(7, 0),
            XlsbTestRecords.EndSheetData());

        var sharedStrings = new XlsbSharedStringTable(["shared"]);
        ExcelRow row = XlsbWorksheetScanner.ScanRows(
            stream,
            sharedStrings,
            StyleTable.Default,
            isDate1904: false,
            ReadMode.Values,
            ExcelRange.Unbounded).Single();

        Assert.Equal(1, row.RowIndex);
        Assert.Equal(2, row.StartColumn);
        Assert.Equal(6, row.CellCount);
        Assert.Equal(42.0, row.GetCell(2).AsNumber());
        Assert.Equal(3.25, row.GetCell(3).AsNumber());
        Assert.True(row.GetCell(4).AsBoolean());
        Assert.Equal("#REF!", row.GetCell(5).AsError());
        Assert.Equal("inline", row.GetCell(6).AsText());
        Assert.Equal("shared", row.GetCell(7).AsText());
        Assert.True(row.GetCell(7).TryGetSharedStringId(out int sharedStringIndex));
        Assert.Equal(0, sharedStringIndex);
    }

    [Fact]
    public void ScanRows_DecodesFormulaCachedValues()
    {
        using var stream = XlsbTestRecords.Stream(
            XlsbTestRecords.Row(2),
            XlsbTestRecords.FormulaNumber(1, 10.5),
            XlsbTestRecords.FormulaString(2, "cached"),
            XlsbTestRecords.FormulaBool(3, true),
            XlsbTestRecords.FormulaError(4, 0x07),
            XlsbTestRecords.EndSheetData());

        ExcelRow row = XlsbWorksheetScanner.ScanRows(
            stream,
            XlsbSharedStringTable.Empty,
            StyleTable.Default,
            isDate1904: false,
            ReadMode.Values,
            ExcelRange.Unbounded).Single();

        Assert.Equal(2, row.RowIndex);
        Assert.Equal(10.5, row.GetCell(1).AsNumber());
        Assert.Equal("cached", row.GetCell(2).AsText());
        Assert.True(row.GetCell(3).AsBoolean());
        Assert.Equal("#DIV/0!", row.GetCell(4).AsError());
    }

    [Fact]
    public void ScanRows_InFormulaMode_ReturnsFormulaText()
    {
        byte[] formula = XlsbTestRecords.CellFormula(
            XlsbTestRecords.FormulaRef(row: 1, column: 1),
            XlsbTestRecords.FormulaInt(1),
            [0x03]);
        using var stream = XlsbTestRecords.Stream(
            XlsbTestRecords.Row(2),
            XlsbTestRecords.FormulaNumber(1, 10.5, formula),
            XlsbTestRecords.EndSheetData());

        ExcelRow row = XlsbWorksheetScanner.ScanRows(
            stream,
            XlsbSharedStringTable.Empty,
            StyleTable.Default,
            isDate1904: false,
            ReadMode.Formulas,
            ExcelRange.Unbounded).Single();

        Assert.Equal(CellType.Formula, row.GetCell(1).CellType);
        Assert.Equal("$A$1+1", row.GetCell(1).AsFormula());
    }

    [Fact]
    public void ScanRows_AppliesRangeAndPreservesColumnOffsets()
    {
        using var stream = XlsbTestRecords.Stream(
            XlsbTestRecords.Row(1),
            XlsbTestRecords.Real(1, 1),
            XlsbTestRecords.Row(2),
            XlsbTestRecords.Real(2, 2),
            XlsbTestRecords.Real(4, 4),
            XlsbTestRecords.Real(5, 5),
            XlsbTestRecords.Row(3),
            XlsbTestRecords.Real(2, 20),
            XlsbTestRecords.EndSheetData());

        var range = new ExcelRange(new ExcelAddress(2, 2), new ExcelAddress(4, 2));
        ExcelRow row = XlsbWorksheetScanner.ScanRows(
            stream,
            XlsbSharedStringTable.Empty,
            StyleTable.Default,
            isDate1904: false,
            ReadMode.Values,
            range).Single();

        Assert.Equal(2, row.RowIndex);
        Assert.Equal(2, row.StartColumn);
        Assert.Equal(3, row.CellCount);
        Assert.Equal(2.0, row.GetCell(2).AsNumber());
        Assert.True(row.GetCell(3).IsEmpty);
        Assert.Equal(4.0, row.GetCell(4).AsNumber());
        Assert.True(row.GetCell(5).IsEmpty);
    }

    [Fact]
    public void ScanRows_ConvertsDateStylesWhenStyleTableIsProvided()
    {
        using var stream = XlsbTestRecords.Stream(
            XlsbTestRecords.Row(1),
            XlsbTestRecords.Real(1, 61, styleIndex: 1),
            XlsbTestRecords.EndSheetData());

        var styles = new StyleTable([FormatClass.General, FormatClass.Date]);
        ExcelRow row = XlsbWorksheetScanner.ScanRows(
            stream,
            XlsbSharedStringTable.Empty,
            styles,
            isDate1904: false,
            ReadMode.Values,
            ExcelRange.Unbounded).Single();

        Assert.Equal(new DateTime(1900, 3, 1), row.GetCell(1).AsDate());
    }

    [Fact]
    public void Cursor_ReusesRowBufferAndSupportsTryParseNext()
    {
        using var stream = XlsbTestRecords.Stream(
            XlsbTestRecords.Row(1),
            XlsbTestRecords.Real(1, 1),
            XlsbTestRecords.Row(2),
            XlsbTestRecords.Real(1, 2),
            XlsbTestRecords.EndSheetData());

        using var cursor = XlsbWorksheetScanner.OpenCursor(
            stream,
            XlsbSharedStringTable.Empty,
            StyleTable.Default,
            isDate1904: false,
            ReadMode.Values,
            ExcelRange.Unbounded);

        Assert.True(cursor.TryParseNext(out ExcelRow first));
        double firstValue = first.GetCell(1).AsNumber();
        Assert.True(cursor.TryParseNext(out ExcelRow second));
        Assert.Equal(1.0, firstValue);
        Assert.Equal(2.0, second.GetCell(1).AsNumber());
        Assert.False(cursor.TryParseNext(out _));
        Assert.True(cursor.IsSheetDone);
    }

    [Fact]
    public void ScanSheet_PushesCellsWithoutMaterializingSharedStringsWhenSinkDoesNotNeedValues()
    {
        using var stream = XlsbTestRecords.Stream(
            XlsbTestRecords.Row(1),
            XlsbTestRecords.SharedString(1, 0),
            XlsbTestRecords.EndSheetData());

        using var sharedStringsStream = XlsbTestRecords.Stream(XlsbTestRecords.SharedStringItem("lazy"));
        var sharedStrings = new Lazy<XlsbSharedStringTable>(() => new XlsbSharedStringTable(sharedStringsStream));
        var sink = new CollectingSink(needsDecodedValue: false, tracksFormulas: false);

        XlsbWorksheetScanner.ScanSheet(
            stream,
            sharedStrings,
            StyleTable.Default,
            isDate1904: false,
            ReadMode.Values,
            ExcelRange.Unbounded,
            ref sink);

        Assert.False(sharedStrings.IsValueCreated);
        Assert.Equal([1], sink.Rows);
        CellEvent cell = Assert.Single(sink.Cells);
        Assert.Equal(1, cell.Column);
        Assert.Equal(CellDataKind.SharedString, cell.Kind);
        Assert.Equal(0, cell.RawIndex);
        Assert.True(cell.Value.IsEmpty);
        Assert.True(sink.Ended);
    }

    [Fact]
    public void ScanSheet_MaterializesSharedStringWhenSinkNeedsDecodedValue()
    {
        using var stream = XlsbTestRecords.Stream(
            XlsbTestRecords.Row(1),
            XlsbTestRecords.SharedString(1, 0),
            XlsbTestRecords.EndSheetData());

        var sharedStrings = new Lazy<XlsbSharedStringTable>(() => new XlsbSharedStringTable(["decoded"]));
        var sink = new CollectingSink(needsDecodedValue: true, tracksFormulas: false);

        XlsbWorksheetScanner.ScanSheet(
            stream,
            sharedStrings,
            StyleTable.Default,
            isDate1904: false,
            ReadMode.Values,
            ExcelRange.Unbounded,
            ref sink);

        CellEvent cell = Assert.Single(sink.Cells);
        Assert.True(sharedStrings.IsValueCreated);
        Assert.Equal("decoded", cell.Value.AsText());
        Assert.Equal(0, cell.RawIndex);
    }

    [Fact]
    public void ScanSheet_EmitsFormulaBeforeFormulaCell()
    {
        using var stream = XlsbTestRecords.Stream(
            XlsbTestRecords.Row(1),
            XlsbTestRecords.FormulaNumber(2, 12),
            XlsbTestRecords.EndSheetData());

        var sink = new CollectingSink(needsDecodedValue: true, tracksFormulas: true);

        XlsbWorksheetScanner.ScanSheet(
            stream,
            XlsbSharedStringTable.Empty,
            StyleTable.Default,
            isDate1904: false,
            ReadMode.Values,
            ExcelRange.Unbounded,
            ref sink);

        FormulaEvent formula = Assert.Single(sink.Formulas);
        Assert.Equal(2, formula.Column);
        Assert.False(formula.IsArray);
        Assert.Equal(2, Assert.Single(sink.Cells).Column);
        Assert.Equal(["row:1", "formula:2", "cell:2"], sink.Events);
    }

    [Fact]
    public void ScanSheet_AppliesRangeAndEmitsBlankStyledCells()
    {
        using var stream = XlsbTestRecords.Stream(
            XlsbTestRecords.Row(1),
            XlsbTestRecords.Real(2, 1),
            XlsbTestRecords.Row(2),
            XlsbTestRecords.Blank(2, styleIndex: 3),
            XlsbTestRecords.Real(3, 3),
            XlsbTestRecords.Row(3),
            XlsbTestRecords.Real(2, 20),
            XlsbTestRecords.EndSheetData());

        var range = new ExcelRange(new ExcelAddress(2, 2), new ExcelAddress(3, 2));
        var sink = new CollectingSink(needsDecodedValue: true, tracksFormulas: false);

        XlsbWorksheetScanner.ScanSheet(
            stream,
            XlsbSharedStringTable.Empty,
            StyleTable.Default,
            isDate1904: false,
            ReadMode.Values,
            range,
            ref sink);

        Assert.Equal([2], sink.Rows);
        Assert.Equal(2, sink.Cells.Count);
        CellEvent blankCell = sink.Cells[0];
        Assert.Equal(2, blankCell.Column);
        Assert.Equal(3, blankCell.StyleIndex);
        Assert.True(blankCell.Value.IsEmpty);
        Assert.Equal(3, sink.Cells[1].Column);
    }

    [Fact]
    public void ScanSheet_EmitsExactMetadataRecords()
    {
        using var stream = XlsbTestRecords.Stream(
            XlsbTestRecords.Dimension(2, 3, 10, 6),
            XlsbTestRecords.Row(2),
            XlsbTestRecords.Real(3, 1),
            XlsbTestRecords.EndSheetData(),
            XlsbTestRecords.MergeCell(4, 2, 5, 3),
            XlsbTestRecords.ConditionalFormatting(),
            XlsbTestRecords.DataValidation(),
            XlsbTestRecords.Hyperlink());
        var sink = new CollectingSink(needsDecodedValue: false, tracksFormulas: false);

        XlsbWorksheetScanner.ScanSheet(
            stream,
            XlsbSharedStringTable.Empty,
            StyleTable.Default,
            isDate1904: false,
            ReadMode.Values,
            ExcelRange.Unbounded,
            ref sink);

        Assert.Equal(new ExcelRange(new ExcelAddress(3, 2), new ExcelAddress(6, 10)), sink.Dimension);
        MergedRegion region = Assert.Single(sink.MergedRegions);
        Assert.Equal(4, region.StartRow);
        Assert.Equal(2, region.StartColumn);
        Assert.Equal(5, region.EndRow);
        Assert.Equal(3, region.EndColumn);
        Assert.Equal(1, sink.ConditionalFormattingCount);
        Assert.Equal(1, sink.DataValidationCount);
        Assert.Equal(1, sink.HyperlinkCount);
    }

    private sealed record CellEvent(
        int Column,
        CellDataKind Kind,
        int StyleIndex,
        ExcelCellValue Value,
        int RawIndex);

    private sealed record FormulaEvent(int Column, bool IsArray);

    [StructLayout(LayoutKind.Auto)]
    private struct CollectingSink : IByteSheetSink
    {
        private readonly bool _needsDecodedValue;
        private readonly bool _tracksFormulas;

        internal CollectingSink(bool needsDecodedValue, bool tracksFormulas)
        {
            _needsDecodedValue = needsDecodedValue;
            _tracksFormulas = tracksFormulas;
            Rows = [];
            Cells = [];
            Formulas = [];
            Events = [];
            MergedRegions = [];
            Dimension = null;
            ConditionalFormattingCount = 0;
            DataValidationCount = 0;
            HyperlinkCount = 0;
            Ended = false;
        }

        public bool NeedsDecodedValue => _needsDecodedValue;
        public bool TracksFormulas => _tracksFormulas;
        internal List<int> Rows { get; }
        internal List<CellEvent> Cells { get; }
        internal List<FormulaEvent> Formulas { get; }
        internal List<string> Events { get; }
        internal List<MergedRegion> MergedRegions { get; }
        internal ExcelRange? Dimension { get; private set; }
        internal int ConditionalFormattingCount { get; private set; }
        internal int DataValidationCount { get; private set; }
        internal int HyperlinkCount { get; private set; }
        internal bool Ended { get; private set; }

        public void OnDimension(in ExcelRange dimension) { Dimension = dimension; }

        public void OnRowStart(int rowIndex)
        {
            Rows.Add(rowIndex);
            Events.Add($"row:{rowIndex}");
        }

        public bool OnCell(int column, CellDataKind kind, int styleIdx, ExcelCellValue value, int rawIndex)
        {
            Cells.Add(new CellEvent(column, kind, styleIdx, value, rawIndex));
            Events.Add($"cell:{column}");
            return true;
        }

        public void OnFormula(int column, bool isArray)
        {
            Formulas.Add(new FormulaEvent(column, isArray));
            Events.Add($"formula:{column}");
        }

        public void OnMergeCell(in MergedRegion region) { MergedRegions.Add(region); }
        public void OnConditionalFormatting() { ConditionalFormattingCount++; }
        public void OnDataValidation() { DataValidationCount++; }
        public void OnHyperlink() { HyperlinkCount++; }
        public void OnEnd() { Ended = true; }
    }
}
