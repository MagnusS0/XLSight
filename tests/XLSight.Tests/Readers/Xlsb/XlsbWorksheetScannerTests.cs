using System.Buffers.Binary;
using System.Runtime.InteropServices;
using XLSight.Internal.Metadata;
using XLSight.Internal.Readers;
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
        ExcelRow row = ScanRows(
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

        ExcelRow row = ScanRows(
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

        ExcelRow row = ScanRows(
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
    public void ScanRows_InFormulaMode_Decodes3dReferences()
    {
        var context = new XlsbFormulaContext(
            [new XlsbSheetInfo("Source", "source.bin"), new XlsbSheetInfo("Lookup Data", "lookup.bin")],
            [new XlsbExternSheetInfo(0, 1, 1)]);
        byte[] cellFormula = XlsbTestRecords.CellFormula(
            XlsbTestRecords.FormulaRef3d(externSheetIndex: 0, row: 1, column: 2),
            XlsbTestRecords.FormulaInt(1),
            [0x03]);
        byte[] areaFormula = XlsbTestRecords.CellFormula(
            XlsbTestRecords.FormulaArea3d(
                externSheetIndex: 0,
                firstRow: 1,
                firstColumn: 1,
                lastRow: 2,
                lastColumn: 2));
        using var stream = XlsbTestRecords.Stream(
            XlsbTestRecords.Row(1),
            XlsbTestRecords.FormulaNumber(1, 0, cellFormula),
            XlsbTestRecords.FormulaNumber(2, 0, areaFormula),
            XlsbTestRecords.EndSheetData());

        ExcelRow row = ScanRows(
            stream,
            XlsbSharedStringTable.Empty,
            StyleTable.Default,
            isDate1904: false,
            ReadMode.Formulas,
            ExcelRange.Unbounded,
            context).Single();

        Assert.Equal("'Lookup Data'!$B$1+1", row.GetCell(1).AsFormula());
        Assert.Equal("'Lookup Data'!$A$1:$B$2", row.GetCell(2).AsFormula());
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
        ExcelRow row = ScanRows(
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
        ExcelRow row = ScanRows(
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

        using var cursor = new XlsbSheetCursor(
            stream,
            new Lazy<XlsbSharedStringTable>(() => XlsbSharedStringTable.Empty),
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
    public void Cursor_UnboundedRange_PreservesOutOfOrderCellColumns()
    {
        using var stream = XlsbTestRecords.Stream(
            XlsbTestRecords.Row(1),
            XlsbTestRecords.Real(5, 5),
            XlsbTestRecords.Real(2, 2),
            XlsbTestRecords.EndSheetData());

        using var cursor = new XlsbSheetCursor(
            stream,
            new Lazy<XlsbSharedStringTable>(() => XlsbSharedStringTable.Empty),
            StyleTable.Default,
            isDate1904: false,
            ReadMode.Values,
            ExcelRange.Unbounded);

        Assert.True(cursor.MoveNext());
        ExcelRow row = cursor.Current;
        Assert.Equal(2, row.StartColumn);
        Assert.Equal(4, row.CellCount);
        Assert.Equal(2, row.GetCell(2).AsNumber());
        Assert.Equal(5, row.GetCell(5).AsNumber());
    }

    [Fact]
    public void Cursor_WithProjection_SkipsValuesButKeepsCellPositions()
    {
        using var stream = XlsbTestRecords.Stream(
            XlsbTestRecords.Row(1),
            XlsbTestRecords.SharedString(1, 0),
            XlsbTestRecords.Real(2, 42),
            XlsbTestRecords.SharedString(3, 0),
            XlsbTestRecords.EndSheetData());

        using var cursor = new XlsbSheetCursor(
            stream,
            new Lazy<XlsbSharedStringTable>(() => new XlsbSharedStringTable(["shared"])),
            StyleTable.Default,
            isDate1904: false,
            ReadMode.Values,
            ExcelRange.Unbounded,
            formulaContext: null,
            projection: new RowProjection([2]));

        Assert.True(cursor.MoveNext());
        ExcelRow row = cursor.Current;
        // The window still spans columns 1..3, but only the projected column carries a value.
        Assert.Equal(1, row.StartColumn);
        Assert.Equal(3, row.CellCount);
        Assert.True(row.GetCell(1).IsEmpty);
        Assert.Equal(42.0, row.GetCell(2).AsNumber());
        Assert.True(row.GetCell(3).IsEmpty);
        Assert.False(cursor.MoveNext());
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
            new Lazy<XlsbSharedStringTable>(() => XlsbSharedStringTable.Empty),
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
    public void ScanSheet_Emits3dFormulaReferenceForAnalysis()
    {
        var context = new XlsbFormulaContext(
            [new XlsbSheetInfo("Source", "source.bin"), new XlsbSheetInfo("Lookup", "lookup.bin")],
            [new XlsbExternSheetInfo(0, 1, 1)]);
        byte[] formula = XlsbTestRecords.CellFormula(
            XlsbTestRecords.FormulaRef3d(externSheetIndex: 0, row: 1, column: 1));
        using var stream = XlsbTestRecords.Stream(
            XlsbTestRecords.Row(1),
            XlsbTestRecords.FormulaNumber(1, 0, formula),
            XlsbTestRecords.EndSheetData());
        var sink = new CollectingSink(
            needsDecodedValue: false,
            tracksFormulas: true,
            tracksFormulaReferences: true);

        XlsbWorksheetScanner.ScanSheet(
            stream,
            new Lazy<XlsbSharedStringTable>(() => XlsbSharedStringTable.Empty),
            StyleTable.Default,
            isDate1904: false,
            ReadMode.Values,
            ExcelRange.Unbounded,
            ref sink,
            context);

        Assert.Equal(["Lookup"], sink.ReferenceSheets);
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
            new Lazy<XlsbSharedStringTable>(() => XlsbSharedStringTable.Empty),
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
            new Lazy<XlsbSharedStringTable>(() => XlsbSharedStringTable.Empty),
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

    [Fact]
    public void ScanSheet_EmitsDetailedDataValidation()
    {
        const uint flags = 3u | (1u << 4) | (1u << 8) | (1u << 9) | (1u << 18) | (1u << 19);
        byte[] formula1 = XlsbTestRecords.CellFormula(XlsbTestRecords.FormulaRef(row: 1, column: 4));
        byte[] formula2 = XlsbTestRecords.CellFormula();
        using var payload = new MemoryStream();
        Span<byte> buf4 = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32LittleEndian(buf4, flags);
        payload.Write(buf4);
        payload.Write(XlsbTestRecords.NullableWideString("Invalid"));
        payload.Write(XlsbTestRecords.NullableWideString("Choose a listed value"));
        payload.Write(XlsbTestRecords.NullableWideString("Selection"));
        payload.Write(XlsbTestRecords.NullableWideString("Pick one"));
        payload.Write(formula1);
        payload.Write(formula2);
        BinaryPrimitives.WriteUInt32LittleEndian(buf4, 1u);
        payload.Write(buf4);
        payload.Write(CreateRangePayload(firstRow: 2, firstColumn: 1, lastRow: 4, lastColumn: 1));

        using var stream = XlsbTestRecords.Stream(
            XlsbTestRecords.EndSheetData(),
            XlsbTestRecords.Record(XlsbRecordType.BrtDVal, payload.ToArray()));
        var sink = new CollectingSink(needsDecodedValue: false, tracksFormulas: false);

        XlsbWorksheetScanner.ScanSheet(
            stream,
            new Lazy<XlsbSharedStringTable>(() => XlsbSharedStringTable.Empty),
            StyleTable.Default,
            isDate1904: false,
            ReadMode.Values,
            ExcelRange.Unbounded,
            ref sink);

        DataValidationInfo validation = Assert.Single(sink.DataValidations);
        Assert.Equal(DataValidationType.List, validation.Type);
        Assert.Equal("A2:A4", validation.SequenceOfReferences);
        Assert.Equal("$D$1", validation.Formula1);
        Assert.Null(validation.Operator);
        Assert.True(validation.AllowBlank);
        Assert.True(validation.ShowDropDown);
        Assert.True(validation.ShowInputMessage);
        Assert.True(validation.ShowErrorMessage);
        Assert.Equal(DataValidationErrorStyle.Warning, validation.ErrorStyle);
        Assert.Equal("Invalid", validation.ErrorTitle);
        Assert.Equal("Choose a listed value", validation.ErrorMessage);
        Assert.Equal("Selection", validation.PromptTitle);
        Assert.Equal("Pick one", validation.PromptMessage);
    }

    /// <summary>
    /// Collects all rows through <see cref="XlsbSheetCursor"/>, snapshotting
    /// each row so the pooled cursor buffer can be reused safely.
    /// </summary>
    private static List<ExcelRow> ScanRows(
        Stream stream,
        XlsbSharedStringTable sharedStrings,
        StyleTable styles,
        bool isDate1904,
        ReadMode mode,
        ExcelRange range,
        XlsbFormulaContext? formulaContext = null)
    {
        using XlsbSheetCursor cursor = new(
            stream,
            new Lazy<XlsbSharedStringTable>(() => sharedStrings),
            styles,
            isDate1904,
            mode,
            range,
            formulaContext);

        var rows = new List<ExcelRow>();
        while (cursor.MoveNext())
        {
            rows.Add(cursor.Current.ToSnapshot());
        }

        return rows;
    }

    private static byte[] CreateRangePayload(int firstRow, int firstColumn, int lastRow, int lastColumn)
    {
        byte[] payload = new byte[16];
        BinaryPrimitives.WriteInt32LittleEndian(payload.AsSpan(0, 4), firstRow - 1);
        BinaryPrimitives.WriteInt32LittleEndian(payload.AsSpan(4, 4), lastRow - 1);
        BinaryPrimitives.WriteInt32LittleEndian(payload.AsSpan(8, 4), firstColumn - 1);
        BinaryPrimitives.WriteInt32LittleEndian(payload.AsSpan(12, 4), lastColumn - 1);
        return payload;
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
        private readonly bool _tracksFormulaReferences;

        internal CollectingSink(
            bool needsDecodedValue,
            bool tracksFormulas,
            bool tracksFormulaReferences = false)
        {
            _needsDecodedValue = needsDecodedValue;
            _tracksFormulas = tracksFormulas;
            _tracksFormulaReferences = tracksFormulaReferences;
            Rows = [];
            Cells = [];
            Formulas = [];
            Events = [];
            MergedRegions = [];
            DataValidations = [];
            ReferenceSheets = [];
            Dimension = null;
            ConditionalFormattingCount = 0;
            DataValidationCount = 0;
            HyperlinkCount = 0;
            Ended = false;
        }

        public bool NeedsDecodedValue => _needsDecodedValue;
        public bool TracksFormulas => _tracksFormulas;
        public bool TracksFormulaReferences => _tracksFormulaReferences;
        internal List<int> Rows { get; }
        internal List<CellEvent> Cells { get; }
        internal List<FormulaEvent> Formulas { get; }
        internal List<string> Events { get; }
        internal List<MergedRegion> MergedRegions { get; }
        internal List<DataValidationInfo> DataValidations { get; }
        internal List<string> ReferenceSheets { get; }
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

        public void OnFormulaReference(in FormulaReference reference)
        {
            if (reference.Sheet is not null)
            {
                ReferenceSheets.Add(reference.Sheet);
            }
        }
        public void OnSharedFormulaDefinition(int sharedIndex) { }
        public void OnSharedFormulaReference(int sharedIndex) { }

        public void OnMergeCell(in MergedRegion region) { MergedRegions.Add(region); }
        public void OnConditionalFormatting() { ConditionalFormattingCount++; }
        public void OnDataValidation(DataValidationInfo? validation)
        {
            DataValidationCount++;
            if (validation is not null)
            {
                DataValidations.Add(validation);
            }
        }
        public void OnHyperlink() { HyperlinkCount++; }
        public void OnEnd() { Ended = true; }
    }
}
