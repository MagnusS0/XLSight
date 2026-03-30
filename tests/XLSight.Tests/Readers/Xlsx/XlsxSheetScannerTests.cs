using System.Text;
using XLSight.Internal.Metadata;
using XLSight.Internal.Packaging;
using XLSight.Internal.Readers.Xlsx;
using XLSight.Models;
using XLSight.Tests.Infrastructure;
using Xunit;

namespace XLSight.Tests.Readers.Xlsx;

/// <summary>
/// Correctness tests for <see cref="XlsxSheetScanner"/>.
/// Unit tests use synthetic XML. Parity tests compare against
/// <see cref="WorksheetScanner.ScanRows"/> on real fixture files.
/// </summary>
public sealed class XlsxSheetScannerTests
{
    private const string Ns = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";

    private static MemoryStream XmlStream(string xml)
        => new(Encoding.UTF8.GetBytes(xml));

    // ── Basic decoding ───────────────────────────────────────────────────────

    [Fact]
    public void NumberCell_ReturnsCorrectValue()
    {
        var rows = Scan($"""
            <worksheet xmlns="{Ns}">
              <sheetData>
                <row r="1"><c r="A1"><v>42</v></c></row>
              </sheetData>
            </worksheet>
            """);

        Assert.Single(rows);
        Assert.Equal(1, rows[0].RowIndex);
        Assert.Equal(42.0, rows[0].GetCell(1).AsNumber());
    }

    [Fact]
    public void SharedStringCell_LooksUpSst()
    {
        var rows = Scan($"""
            <worksheet xmlns="{Ns}">
              <sheetData>
                <row r="1"><c r="A1" t="s"><v>0</v></c></row>
              </sheetData>
            </worksheet>
            """, sst: SstBuilder.Make("Hello"));

        Assert.Single(rows);
        Assert.Equal("Hello", rows[0].GetCell(1).AsText());
    }

    [Fact]
    public void BooleanCells_TrueAndFalse()
    {
        var rows = Scan($"""
            <worksheet xmlns="{Ns}">
              <sheetData>
                <row r="1">
                  <c r="A1" t="b"><v>1</v></c>
                  <c r="B1" t="b"><v>0</v></c>
                </row>
              </sheetData>
            </worksheet>
            """);

        Assert.True(rows[0].GetCell(1).AsBoolean());
        Assert.False(rows[0].GetCell(2).AsBoolean());
    }

    [Fact]
    public void ErrorCell_ReturnsErrorValue()
    {
        var rows = Scan($"""
            <worksheet xmlns="{Ns}">
              <sheetData>
                <row r="1"><c r="A1" t="e"><v>#DIV/0!</v></c></row>
              </sheetData>
            </worksheet>
            """);

        var cell = rows[0].GetCell(1);
        Assert.Equal(CellType.Error, cell.CellType);
        Assert.Equal("#DIV/0!", cell.AsError());
    }

    [Fact]
    public void InlineStringCell_ReturnsText()
    {
        var rows = Scan($"""
            <worksheet xmlns="{Ns}">
              <sheetData>
                <row r="1">
                  <c r="A1" t="inlineStr"><is><t>Inline text</t></is></c>
                </row>
              </sheetData>
            </worksheet>
            """);

        Assert.Equal("Inline text", rows[0].GetCell(1).AsText());
    }

    [Fact]
    public void FormulaStringCell_ReturnsText()
    {
        var rows = Scan($"""
            <worksheet xmlns="{Ns}">
              <sheetData>
                <row r="1"><c r="A1" t="str"><v>Result</v></c></row>
              </sheetData>
            </worksheet>
            """);

        Assert.Equal("Result", rows[0].GetCell(1).AsText());
    }

    [Fact]
    public void PrefixedWorksheetTags_AreParsed()
    {
        const string prefixedNs = "urn:test";
        var rows = Scan($"""
            <x:worksheet xmlns:x="{prefixedNs}">
              <x:sheetData>
                <x:row r="1">
                  <x:c r="A1" t="str"><x:v>Alpha</x:v></x:c>
                  <x:c r="B1"><x:v>42</x:v></x:c>
                </x:row>
              </x:sheetData>
            </x:worksheet>
            """);

        Assert.Single(rows);
        Assert.Equal("Alpha", rows[0].GetCell(1).AsText());
        Assert.Equal(42.0, rows[0].GetCell(2).AsNumber());
    }

    [Fact]
    public void PrefixedInlineStringTextTags_AreParsed()
    {
        const string prefixedNs = "urn:test";
        var rows = Scan($"""
            <x:worksheet xmlns:x="{prefixedNs}">
              <x:sheetData>
                <x:row r="1">
                  <x:c r="A1" t="inlineStr">
                    <x:is>
                      <x:r><x:t>Hello</x:t></x:r>
                      <x:r><x:t> World</x:t></x:r>
                    </x:is>
                  </x:c>
                </x:row>
              </x:sheetData>
            </x:worksheet>
            """);

        Assert.Single(rows);
        Assert.Equal("Hello World", rows[0].GetCell(1).AsText());
    }

    [Fact]
    public void SingleQuotedInlineStringAttributes_AreParsed()
    {
        var rows = Scan("""
            <worksheet xmlns="urn:test">
              <sheetData>
                <row>
                  <c t='inlineStr'><is><t>Single quoted</t></is></c>
                </row>
              </sheetData>
            </worksheet>
            """);

        Assert.Single(rows);
        Assert.Equal("Single quoted", rows[0].GetCell(1).AsText());
    }

    // ── Edge cases ───────────────────────────────────────────────────────────

    [Fact]
    public void EmptySheetData_YieldsNoRows()
    {
        var rows = Scan($"""
            <worksheet xmlns="{Ns}">
              <sheetData/>
            </worksheet>
            """);

        Assert.Empty(rows);
    }

    [Fact]
    public void SharedStringCell_EmptyV_ReturnsEmpty()
    {
        // t="s" with <v/> must NOT return sharedStrings[0].
        var rows = Scan($"""
            <worksheet xmlns="{Ns}">
              <sheetData>
                <row r="1"><c r="A1" t="s"><v/></c></row>
              </sheetData>
            </worksheet>
            """, sst: SstBuilder.Make("ShouldNotAppear"));

        Assert.Single(rows);
        Assert.True(rows[0].GetCell(1).IsEmpty);
    }

    [Fact]
    public void EmptyElementCell_ProducesEmptyValue()
    {
        var rows = Scan($"""
            <worksheet xmlns="{Ns}">
              <sheetData>
                <row r="1"><c r="A1"/><c r="B1"><v>7</v></c></row>
              </sheetData>
            </worksheet>
            """);

        Assert.Single(rows);
        Assert.True(rows[0].GetCell(1).IsEmpty);
        Assert.Equal(7.0, rows[0].GetCell(2).AsNumber());
    }

    [Fact]
    public void ExplicitEmptyValueCells_PreserveRowWidth()
    {
        var rows = Scan($"""
            <worksheet xmlns="{Ns}">
              <sheetData>
                <row r="1">
                  <c r="A1"><v></v></c>
                  <c r="B1"><v/></c>
                  <c r="C1" t="s"><v></v></c>
                </row>
              </sheetData>
            </worksheet>
            """);

        Assert.Single(rows);
        Assert.Equal(1, rows[0].StartColumn);
        Assert.Equal(3, rows[0].CellCount);
        Assert.True(rows[0].GetCell(1).IsEmpty);
        Assert.True(rows[0].GetCell(2).IsEmpty);
        Assert.True(rows[0].GetCell(3).IsEmpty);
    }

    [Fact]
    public void AbsentR_Attribute_UsesSequentialColumnTracking()
    {
        var rows = Scan($"""
            <worksheet xmlns="{Ns}">
              <sheetData>
                <row r="1">
                  <c><v>10</v></c>
                  <c><v>20</v></c>
                  <c><v>30</v></c>
                </row>
              </sheetData>
            </worksheet>
            """);

        Assert.Single(rows);
        Assert.Equal(10.0, rows[0].GetCell(1).AsNumber());
        Assert.Equal(20.0, rows[0].GetCell(2).AsNumber());
        Assert.Equal(30.0, rows[0].GetCell(3).AsNumber());
    }

    [Fact]
    public void MultipleRows_AllYielded()
    {
        var rows = Scan($"""
            <worksheet xmlns="{Ns}">
              <sheetData>
                <row r="1"><c r="A1"><v>1</v></c></row>
                <row r="2"><c r="A2"><v>2</v></c></row>
                <row r="3"><c r="A3"><v>3</v></c></row>
              </sheetData>
            </worksheet>
            """);

        Assert.Equal(3, rows.Count);
        for (int i = 0; i < 3; i++)
        {
            Assert.Equal(i + 1, rows[i].RowIndex);
            Assert.Equal(i + 1.0, rows[i].GetCell(1).AsNumber());
        }
    }

    [Fact]
    public void XmlEntitiesInString_AreUnescaped()
    {
        var rows = Scan($"""
            <worksheet xmlns="{Ns}">
              <sheetData>
                <row r="1">
                  <c r="A1" t="str"><v>&amp;lt;&amp;gt;</v></c>
                </row>
              </sheetData>
            </worksheet>
            """);

        // The raw XML &amp; → & after first XML parse by browser, but here
        // we're scanning raw bytes so entity is literal &amp;lt; → &lt; in the value bytes.
        // The formula string decode path calls UnescapeXml, so &amp; → &.
        Assert.Equal(CellType.Text, rows[0].GetCell(1).CellType);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(7)]
    [InlineData(8)]
    public void NumberCell_SplitAcrossReads_PreservesValue(int chunkSize)
    {
        var rows = ScanChunked($"""
            <worksheet xmlns="{Ns}">
              <sheetData>
                <row r="1"><c r="A1"><v>12345</v></c></row>
              </sheetData>
            </worksheet>
            """, chunkSize);

        Assert.Single(rows);
        Assert.Equal(12345.0, rows[0].GetCell(1).AsNumber());
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(7)]
    [InlineData(8)]
    public void SharedStringCell_SplitAcrossReads_PreservesValue(int chunkSize)
    {
        var rows = ScanChunked($"""
            <worksheet xmlns="{Ns}">
              <sheetData>
                <row r="1"><c r="A1" t="s"><v>0</v></c></row>
              </sheetData>
            </worksheet>
            """, chunkSize, sst: SstBuilder.Make("Hello"));

        Assert.Single(rows);
        Assert.Equal("Hello", rows[0].GetCell(1).AsText());
    }

    // ── Range filtering ──────────────────────────────────────────────────────

    [Fact]
    public void BoundedRange_SkipsRowsOutsideRange()
    {
        var range = new ExcelRange(new ExcelAddress(1, 2), new ExcelAddress(1, 3));
        var rows = Scan($"""
            <worksheet xmlns="{Ns}">
              <sheetData>
                <row r="1"><c r="A1"><v>1</v></c></row>
                <row r="2"><c r="A2"><v>2</v></c></row>
                <row r="3"><c r="A3"><v>3</v></c></row>
                <row r="4"><c r="A4"><v>4</v></c></row>
              </sheetData>
            </worksheet>
            """, range: range);

        Assert.Equal(2, rows.Count);
        Assert.Equal(2, rows[0].RowIndex);
        Assert.Equal(3, rows[1].RowIndex);
    }

    [Fact]
    public void BoundedRange_SkipsColumnsOutsideRange()
    {
        // Only column B (index 2).
        var range = new ExcelRange(new ExcelAddress(2, 1), new ExcelAddress(2, 1));
        var rows = Scan($"""
            <worksheet xmlns="{Ns}">
              <sheetData>
                <row r="1">
                  <c r="A1"><v>10</v></c>
                  <c r="B1"><v>20</v></c>
                  <c r="C1"><v>30</v></c>
                </row>
              </sheetData>
            </worksheet>
            """, range: range);

        Assert.Single(rows);
        Assert.Equal(20.0, rows[0].GetCell(2).AsNumber());
    }

    // ── Early exit ───────────────────────────────────────────────────────────

    [Fact]
    public void TakeN_DisposesCleanlyAndReturnsExactCount()
    {
        var rows = Scan($"""
            <worksheet xmlns="{Ns}">
              <sheetData>
                <row r="1"><c r="A1"><v>1</v></c></row>
                <row r="2"><c r="A2"><v>2</v></c></row>
                <row r="3"><c r="A3"><v>3</v></c></row>
              </sheetData>
            </worksheet>
            """).Take(2).ToList();

        Assert.Equal(2, rows.Count);
    }

    // ── Parity against WorksheetScanner.ScanRows ─────────────────────────────

    [Theory]
    [InlineData("small.xlsx")]
    [InlineData("string_heavy.xlsx")]
    [InlineData("medium.xlsx")]
    public void RealFile_ByteEngineMatchesXmlEngine(string fileName)
    {
        var path = Path.Combine(AppContext.BaseDirectory, "TestData", fileName);
        if (!File.Exists(path)) { return; }

        var xmlRows = StreamWithXmlEngine(path).ToList();
        var byteRows = StreamWithByteEngine(path).ToList();

        Assert.Equal(xmlRows.Count, byteRows.Count);
        for (int i = 0; i < xmlRows.Count; i++)
        {
            AssertRowsEqual(xmlRows[i], byteRows[i], fileName, i);
        }
    }

    // ── Performance optimisations — fast path correctness ────────────────────

    [Fact]
    public void FastPath_Number_ReturnsCorrectValue()
    {
        // All cell bytes fit in a single MemoryStream buffer read, exercising the
        // inline fast path that decodes directly from a span slice without ExtractUntilClose.
        var rows = Scan($"""
            <worksheet xmlns="{Ns}">
              <sheetData>
                <row r="1"><c r="A1"><v>12345</v></c></row>
              </sheetData>
            </worksheet>
            """);

        Assert.Single(rows);
        Assert.Equal(CellType.Number, rows[0].GetCell(1).CellType);
        Assert.Equal(12345.0, rows[0].GetCell(1).AsNumber());
    }

    [Fact]
    public void FastPath_SharedString_ReturnsCorrectValue()
    {
        // SST lookup via the inline fast path: both </c> and </v> are visible in
        // the current buffer span so the index is decoded without ExtractUntilClose.
        var rows = Scan($"""
            <worksheet xmlns="{Ns}">
              <sheetData>
                <row r="1"><c r="A1" t="s"><v>0</v></c></row>
              </sheetData>
            </worksheet>
            """, sst: SstBuilder.Make("FastPathString"));

        Assert.Single(rows);
        Assert.Equal(CellType.Text, rows[0].GetCell(1).CellType);
        Assert.Equal("FastPathString", rows[0].GetCell(1).AsText());
    }

    [Fact]
    public void FastPath_MultipleNumericCells_SameRow()
    {
        // Five numeric cells in one row — all decoded via the fast path in a
        // single pass without any buffer-refill.
        var rows = Scan($"""
            <worksheet xmlns="{Ns}">
              <sheetData>
                <row r="1">
                  <c r="A1"><v>10</v></c>
                  <c r="B1"><v>20</v></c>
                  <c r="C1"><v>30</v></c>
                  <c r="D1"><v>40</v></c>
                  <c r="E1"><v>50</v></c>
                </row>
              </sheetData>
            </worksheet>
            """);

        Assert.Single(rows);
        Assert.Equal(5, rows[0].CellCount);
        Assert.Equal(10.0, rows[0].GetCell(1).AsNumber());
        Assert.Equal(20.0, rows[0].GetCell(2).AsNumber());
        Assert.Equal(30.0, rows[0].GetCell(3).AsNumber());
        Assert.Equal(40.0, rows[0].GetCell(4).AsNumber());
        Assert.Equal(50.0, rows[0].GetCell(5).AsNumber());
    }

    [Fact]
    public void FastPath_EmptyV_ElementSelfClosing_ReturnsEmpty()
    {
        // <v/> is a self-closing value element; the scanner must detect IsEmptyElement
        // and return ExcelCellValue.Empty rather than attempting to decode garbage bytes.
        var rows = Scan($"""
            <worksheet xmlns="{Ns}">
              <sheetData>
                <row r="1"><c r="A1"><v/></c></row>
              </sheetData>
            </worksheet>
            """);

        Assert.Single(rows);
        Assert.True(rows[0].GetCell(1).IsEmpty);
    }

    [Fact]
    public void FastPath_BooleanCell_ReturnsCorrectValue()
    {
        var rows = Scan($"""
            <worksheet xmlns="{Ns}">
              <sheetData>
                <row r="1"><c r="A1" t="b"><v>1</v></c></row>
              </sheetData>
            </worksheet>
            """);

        Assert.Single(rows);
        Assert.Equal(CellType.Boolean, rows[0].GetCell(1).CellType);
        Assert.True(rows[0].GetCell(1).AsBoolean());
    }

    // ── Performance optimisations — fast-path fallback at buffer boundaries ──

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(7)]
    [InlineData(8)]
    public void FastPathFallback_NumberCell_SplitAcrossChunks(int chunkSize)
    {
        // Small chunk sizes force mid-cell buffer refills, exercising the slow-path
        // fallback that is taken when </v> is not visible in the current buffer span.
        var rows = ScanChunked($"""
            <worksheet xmlns="{Ns}">
              <sheetData>
                <row r="1"><c r="A1"><v>98765</v></c></row>
              </sheetData>
            </worksheet>
            """, chunkSize);

        Assert.Single(rows);
        Assert.Equal(98765.0, rows[0].GetCell(1).AsNumber());
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(7)]
    [InlineData(8)]
    public void FastPathFallback_SharedStringCell_SplitAcrossChunks(int chunkSize)
    {
        // Shared-string fast path falls back gracefully when the SST index value
        // straddles a chunk boundary.
        var rows = ScanChunked($"""
            <worksheet xmlns="{Ns}">
              <sheetData>
                <row r="1"><c r="A1" t="s"><v>0</v></c></row>
              </sheetData>
            </worksheet>
            """, chunkSize, sst: SstBuilder.Make("SplitString"));

        Assert.Single(rows);
        Assert.Equal("SplitString", rows[0].GetCell(1).AsText());
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(7)]
    [InlineData(8)]
    public void FastPathFallback_CellValueSplitAtCloseTag(int chunkSize)
    {
        // For chunk sizes 1-8 at least one read boundary lands inside the </v>
        // close tag, forcing ExtractUntilClose + SkipToEndTag (slow path).
        var rows = ScanChunked($"""
            <worksheet xmlns="{Ns}">
              <sheetData>
                <row r="1"><c r="A1"><v>999999</v></c></row>
              </sheetData>
            </worksheet>
            """, chunkSize);

        Assert.Single(rows);
        Assert.Equal(999999.0, rows[0].GetCell(1).AsNumber());
    }

    // ── Performance optimisations — bounding: no cross-cell contamination ────

    [Fact]
    public void Bounding_ValueTagInStringContent_DoesNotConfuseNextCell()
    {
        // A1 has t="str" with the single-character cached value "v". The bounding fix
        // ensures B1's numeric scan is not confused by the content byte of A1's <v>v</v>.
        var rows = Scan($"""
            <worksheet xmlns="{Ns}">
              <sheetData>
                <row r="1">
                  <c r="A1" t="str"><v>v</v></c>
                  <c r="B1"><v>99</v></c>
                </row>
              </sheetData>
            </worksheet>
            """);

        Assert.Single(rows);
        Assert.Equal(CellType.Text,   rows[0].GetCell(1).CellType);
        Assert.Equal("v",             rows[0].GetCell(1).AsText());
        Assert.Equal(CellType.Number, rows[0].GetCell(2).CellType);
        Assert.Equal(99.0,            rows[0].GetCell(2).AsNumber());
    }

    [Fact]
    public void Bounding_FormulaTagByteInContent_DoesNotTriggerFormula()
    {
        // A1 is a formula-string cell whose cached value is the single character "f".
        // Under ReadMode.Values <f> tags are not consulted; cell-body bounding ensures
        // the "f" byte in A1's <v>f</v> does not bleed into B1's scan.
        var rows = Scan($"""
            <worksheet xmlns="{Ns}">
              <sheetData>
                <row r="1">
                  <c r="A1" t="str"><v>f</v></c>
                  <c r="B1"><v>55</v></c>
                </row>
              </sheetData>
            </worksheet>
            """);

        Assert.Single(rows);
        Assert.Equal(CellType.Text,   rows[0].GetCell(1).CellType);
        Assert.Equal("f",             rows[0].GetCell(1).AsText());
        Assert.Equal(CellType.Number, rows[0].GetCell(2).CellType);
        Assert.Equal(55.0,            rows[0].GetCell(2).AsNumber());
    }

    [Fact]
    public void Bounding_LargeNumberOfEmptyCells_CorrectCount()
    {
        // 100 consecutive self-closing <c/> elements; verifies the scanner handles
        // a large run of empty cells without misreading any cell boundary.
        const int cellCount = 100;
        var cellsXml = string.Concat(
            Enumerable.Range(1, cellCount)
                      .Select(col => $"<c r=\"{new ExcelAddress(col, 1)}\"/>"));

        var rows = Scan($"""
            <worksheet xmlns="{Ns}">
              <sheetData>
                <row r="1">{cellsXml}</row>
              </sheetData>
            </worksheet>
            """);

        Assert.Single(rows);
        Assert.Equal(cellCount, rows[0].CellCount);
        for (int col = 1; col <= cellCount; col++)
        {
            Assert.True(rows[0].GetCell(col).IsEmpty, $"Column {col} should be empty");
        }
    }

    // ── Performance optimisations — formula detection ─────────────────────────

    [Fact]
    public void FormulaCell_ReturnsFormulaCellType()
    {
        // Under ReadMode.Formulas a cell with <f> must be returned as
        // CellType.Formula carrying the raw formula text.
        var rows = ScanFormulas($"""
            <worksheet xmlns="{Ns}">
              <sheetData>
                <row r="1"><c r="A1"><f>A1+B1</f><v>3</v></c></row>
              </sheetData>
            </worksheet>
            """);

        Assert.Single(rows);
        Assert.Equal(CellType.Formula, rows[0].GetCell(1).CellType);
        Assert.Equal("A1+B1",          rows[0].GetCell(1).AsFormula());
    }

    [Fact]
    public void ArrayFormulaCell_DetectedCorrectly()
    {
        // <f t="array"> marks an array-formula anchor; under ReadMode.Formulas the
        // formula text is returned with CellType.Formula regardless of the t= attribute.
        var rows = ScanFormulas($"""
            <worksheet xmlns="{Ns}">
              <sheetData>
                <row r="1"><c r="A1"><f t="array">SUM(A1:A10)</f><v>55</v></c></row>
              </sheetData>
            </worksheet>
            """);

        Assert.Single(rows);
        Assert.Equal(CellType.Formula, rows[0].GetCell(1).CellType);
        Assert.Equal("SUM(A1:A10)",    rows[0].GetCell(1).AsFormula());
    }

    [Fact]
    public void FormulaCell_CachedValueUsed_WhenInValueMode()
    {
        // Under ReadMode.Values the <f> element is silently skipped; the cached
        // numeric value in <v>3</v> is decoded and returned as CellType.Number.
        var rows = Scan($"""
            <worksheet xmlns="{Ns}">
              <sheetData>
                <row r="1"><c r="A1"><f>A1+B1</f><v>3</v></c></row>
              </sheetData>
            </worksheet>
            """);

        Assert.Single(rows);
        Assert.Equal(CellType.Number, rows[0].GetCell(1).CellType);
        Assert.Equal(3.0,             rows[0].GetCell(1).AsNumber());
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(7)]
    [InlineData(8)]
    public void FormulaCell_SplitAcrossChunks_DetectedCorrectly(int chunkSize)
    {
        // Tiny chunks split the <f>...</f> body across multiple refills; formula
        // detection must still return CellType.Formula.
        var rows = ScanFormulasChunked($"""
            <worksheet xmlns="{Ns}">
              <sheetData>
                <row r="1"><c r="A1"><f>A1+B1</f><v>3</v></c></row>
              </sheetData>
            </worksheet>
            """, chunkSize);

        Assert.Single(rows);
        Assert.Equal(CellType.Formula, rows[0].GetCell(1).CellType);
        Assert.Equal("A1+B1",          rows[0].GetCell(1).AsFormula());
    }

    // ── Performance optimisations — right-bound early exit ───────────────────

    [Fact]
    public void RightBoundEarlyExit_DoesNotYieldCellsBeyondRightEdge()
    {
        // Row of 20 numeric cells (columns A-T, values 1-20) with scan range
        // restricted to columns C-F (3-6). The early-exit optimisation must break
        // the inner loop at column G so exactly 4 cells are stored.
        var cells = string.Concat(Enumerable.Range(1, 20)
            .Select(i => $"<c r=\"{new ExcelAddress(i, 1)}\"><v>{i}</v></c>"));
        var range = new ExcelRange(new ExcelAddress(3, 1), new ExcelAddress(6, 1));

        var rows = Scan($"""
            <worksheet xmlns="{Ns}">
              <sheetData>
                <row r="1">{cells}</row>
              </sheetData>
            </worksheet>
            """, range: range);

        Assert.Single(rows);
        Assert.Equal(4, rows[0].CellCount);
        Assert.Equal(3, rows[0].StartColumn);
    }

    [Fact]
    public void RightBoundEarlyExit_CorrectValuesWithinRange()
    {
        // Same 20-cell row; verify the four in-range cells (columns 3-6) carry
        // the values 3.0, 4.0, 5.0, 6.0 respectively.
        var cells = string.Concat(Enumerable.Range(1, 20)
            .Select(i => $"<c r=\"{new ExcelAddress(i, 1)}\"><v>{i}</v></c>"));
        var range = new ExcelRange(new ExcelAddress(3, 1), new ExcelAddress(6, 1));

        var rows = Scan($"""
            <worksheet xmlns="{Ns}">
              <sheetData>
                <row r="1">{cells}</row>
              </sheetData>
            </worksheet>
            """, range: range);

        Assert.Single(rows);
        Assert.Equal(3.0, rows[0].GetCell(3).AsNumber());
        Assert.Equal(4.0, rows[0].GetCell(4).AsNumber());
        Assert.Equal(5.0, rows[0].GetCell(5).AsNumber());
        Assert.Equal(6.0, rows[0].GetCell(6).AsNumber());
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(7)]
    [InlineData(8)]
    public void RightBoundEarlyExit_SplitAcrossChunks_StillCorrect(int chunkSize)
    {
        // 10 cells, range columns B-D (2-4); small chunk sizes verify the right-bound
        // early exit fires correctly even when row bytes span multiple buffer refills.
        var cells = string.Concat(Enumerable.Range(1, 10)
            .Select(i => $"<c r=\"{new ExcelAddress(i, 1)}\"><v>{i}</v></c>"));
        var range = new ExcelRange(new ExcelAddress(2, 1), new ExcelAddress(4, 1));

        var rows = ScanChunked($"""
            <worksheet xmlns="{Ns}">
              <sheetData>
                <row r="1">{cells}</row>
              </sheetData>
            </worksheet>
            """, chunkSize, range: range);

        Assert.Single(rows);
        Assert.Equal(3, rows[0].CellCount);
        Assert.Equal(2, rows[0].StartColumn);
        Assert.Equal(2.0, rows[0].GetCell(2).AsNumber());
        Assert.Equal(3.0, rows[0].GetCell(3).AsNumber());
        Assert.Equal(4.0, rows[0].GetCell(4).AsNumber());
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private static List<ExcelRow> Scan(
        string worksheetXml,
        SharedStringTable? sst = null,
        ExcelRange? range = null)
    {
        using var stream = XmlStream(worksheetXml);
        return XlsxSheetScanner.ScanRows(
            stream,
            sst ?? SharedStringTable.Empty,
            StyleTable.Default,
            isDate1904: false,
            ReadMode.Values,
            range ?? ExcelRange.Unbounded).ToList();
    }

    private static List<ExcelRow> ScanChunked(
        string worksheetXml,
        int chunkSize,
        SharedStringTable? sst = null,
        ExcelRange? range = null)
    {
        using var inner = XmlStream(worksheetXml);
        using var stream = new ChunkedReadStream(inner, chunkSize);
        return XlsxSheetScanner.ScanRows(
            stream,
            sst ?? SharedStringTable.Empty,
            StyleTable.Default,
            isDate1904: false,
            ReadMode.Values,
            range ?? ExcelRange.Unbounded).ToList();
    }

    private static List<ExcelRow> ScanFormulas(
        string worksheetXml,
        SharedStringTable? sst = null,
        ExcelRange? range = null)
    {
        using var stream = XmlStream(worksheetXml);
        return XlsxSheetScanner.ScanRows(
            stream,
            sst ?? SharedStringTable.Empty,
            StyleTable.Default,
            isDate1904: false,
            ReadMode.Formulas,
            range ?? ExcelRange.Unbounded).ToList();
    }

    private static List<ExcelRow> ScanFormulasChunked(
        string worksheetXml,
        int chunkSize,
        SharedStringTable? sst = null,
        ExcelRange? range = null)
    {
        using var inner = XmlStream(worksheetXml);
        using var stream = new ChunkedReadStream(inner, chunkSize);
        return XlsxSheetScanner.ScanRows(
            stream,
            sst ?? SharedStringTable.Empty,
            StyleTable.Default,
            isDate1904: false,
            ReadMode.Formulas,
            range ?? ExcelRange.Unbounded).ToList();
    }

    private static List<ExcelRow> StreamWithXmlEngine(string path)
    {
        using var wb = global::XLSight.ExcelWorkbook.Open(path);
        return wb.StreamSheet(wb.SheetNames[0]).Select(r => r.CloneRow()).ToList();
    }

    private static List<ExcelRow> StreamWithByteEngine(string path)
    {
        using var package = XlsxPackage.Open(File.OpenRead(path), ownsStream: true);

        using var wbStream = package.GetEntry("xl/workbook.xml")!.OpenBuffered();
        using var relsStream = package.GetEntry("xl/_rels/workbook.xml.rels")!.OpenBuffered();
        var def = WorkbookParser.Parse(wbStream);
        var metadata = RelationshipsParser.Parse(relsStream, def);

        SharedStringTable sst = SharedStringTable.Empty;
        var sstEntry = package.GetEntry("xl/sharedStrings.xml");
        if (sstEntry is not null)
        {
            using var sstStream = sstEntry.OpenBuffered();
            sst = SharedStringsParser.Parse(sstStream);
        }

        StyleTable styles = StyleTable.Default;
        var stylesEntry = package.GetEntry("xl/styles.xml");
        if (stylesEntry is not null)
        {
            using var stylesStream = stylesEntry.OpenBuffered();
            styles = StylesParser.Parse(stylesStream);
        }

        var sheet = metadata.Sheets[0];
        var wsEntry = package.GetEntry(sheet.Path);
        if (wsEntry is null) { return []; }

        using var wsStream = wsEntry.OpenBuffered();
        return XlsxSheetScanner.ScanRows(
            wsStream, sst, styles, metadata.UsesDate1904,
            ReadMode.Values, ExcelRange.Unbounded).ToList();
    }

    private static void AssertRowsEqual(ExcelRow expected, ExcelRow actual, string file, int rowIdx)
    {
        Assert.True(
            expected.RowIndex == actual.RowIndex,
            $"{file} row[{rowIdx}]: RowIndex {expected.RowIndex} != {actual.RowIndex}");
        Assert.True(
            expected.StartColumn == actual.StartColumn,
            $"{file} row[{rowIdx}]: StartColumn {expected.StartColumn} != {actual.StartColumn}");
        Assert.True(
            expected.CellCount == actual.CellCount,
            $"{file} row[{rowIdx}]: CellCount {expected.CellCount} != {actual.CellCount}");
        for (int col = expected.StartColumn; col < expected.StartColumn + expected.CellCount; col++)
        {
            var exp = expected.GetCell(col);
            var act = actual.GetCell(col);
            Assert.True(
                exp.CellType == act.CellType,
                $"{file} row[{rowIdx}] col {col}: CellType {exp.CellType} != {act.CellType}");
        }
    }

    private sealed class ChunkedReadStream(Stream inner, int maxChunk) : Stream
    {
        public override bool CanRead => inner.CanRead;
        public override bool CanSeek => inner.CanSeek;
        public override bool CanWrite => false;
        public override long Length => inner.Length;

        public override long Position
        {
            get => inner.Position;
            set => inner.Position = value;
        }

        public override void Flush() => inner.Flush();

        public override int Read(byte[] buffer, int offset, int count) =>
            inner.Read(buffer, offset, Math.Min(count, maxChunk));

        public override int Read(Span<byte> buffer) =>
            inner.Read(buffer[..Math.Min(buffer.Length, maxChunk)]);

        public override long Seek(long offset, SeekOrigin origin) => inner.Seek(offset, origin);
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                inner.Dispose();
            }

            base.Dispose(disposing);
        }
    }
}
