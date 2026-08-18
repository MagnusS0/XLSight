using System.Text;
using XLSight.Internal.Metadata;
using XLSight.Internal.Readers;
using XLSight.Internal.Readers.Xlsx;
using XLSight.Tests.Infrastructure;
using Xunit;

namespace XLSight.Tests.Readers.Xlsx;

/// <summary>
/// Correctness tests for <see cref="SheetCursor"/>.
/// Covers cursor lifecycle, buffer-reuse contract, and parity with ScanRows.
/// </summary>
public sealed class SheetCursorTests
{
    private const string Ns = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";

    private static MemoryStream XmlStream(string xml)
        => new(Encoding.UTF8.GetBytes(xml));

    private static SheetCursor OpenCursor(string worksheetXml, SharedStringTable? sst = null, ExcelRange? range = null)
        => XlsxSheetScanner.OpenCursor(
            XmlStream(worksheetXml),
            sst ?? SharedStringTable.Empty,
            StyleTable.Default,
            isDate1904: false,
            ReadMode.Values,
            range ?? ExcelRange.Unbounded);

    // ── Lifecycle ─────────────────────────────────────────────────────────────

    [Fact]
    public void MoveNext_EmptySheet_ReturnsFalse()
    {
        using var cursor = OpenCursor($"""
            <worksheet xmlns="{Ns}">
              <sheetData />
            </worksheet>
            """);

        Assert.False(cursor.MoveNext());
    }

    [Fact]
    public void MoveNext_EmptySheetNoSpace_ReturnsFalse()
    {
        using var cursor = OpenCursor($"""
            <worksheet xmlns="{Ns}"><sheetData/></worksheet>
            """);

        Assert.False(cursor.MoveNext());
    }

    [Fact]
    public void MoveNext_SingleRow_TrueTheFalse()
    {
        using var cursor = OpenCursor($"""
            <worksheet xmlns="{Ns}">
              <sheetData>
                <row r="1"><c r="A1"><v>1</v></c></row>
              </sheetData>
            </worksheet>
            """);

        Assert.True(cursor.MoveNext());
        Assert.False(cursor.MoveNext());
    }

    [Fact]
    public void MoveNext_MultipleRows_CountsCorrectly()
    {
        using var cursor = OpenCursor($"""
            <worksheet xmlns="{Ns}">
              <sheetData>
                <row r="1"><c r="A1"><v>1</v></c></row>
                <row r="2"><c r="A2"><v>2</v></c></row>
                <row r="3"><c r="A3"><v>3</v></c></row>
              </sheetData>
            </worksheet>
            """);

        int count = 0;
        while (cursor.MoveNext()) { count++; }
        Assert.Equal(3, count);
    }

    // ── Current is a live view ────────────────────────────────────────────────

    [Fact]
    public void Current_ReflectsEachRow()
    {
        using var cursor = OpenCursor($"""
            <worksheet xmlns="{Ns}">
              <sheetData>
                <row r="1"><c r="A1"><v>10</v></c></row>
                <row r="2"><c r="A2"><v>20</v></c></row>
              </sheetData>
            </worksheet>
            """);

        cursor.MoveNext();
        Assert.Equal(1, cursor.Current.RowIndex);
        Assert.Equal(10.0, cursor.Current.GetCell(1).AsNumber());

        cursor.MoveNext();
        Assert.Equal(2, cursor.Current.RowIndex);
        Assert.Equal(20.0, cursor.Current.GetCell(1).AsNumber());
    }

    [Fact]
    public void Current_AfterMoveNext_UpdatesValues()
    {
        using var cursor = OpenCursor($"""
            <worksheet xmlns="{Ns}">
              <sheetData>
                <row r="1"><c r="A1"><v>1</v></c></row>
                <row r="2"><c r="A2"><v>999</v></c></row>
              </sheetData>
            </worksheet>
            """);

        cursor.MoveNext();
        double firstVal = cursor.Current.GetCell(1).AsNumber();

        cursor.MoveNext();
        double secondVal = cursor.Current.GetCell(1).AsNumber();

        Assert.Equal(1.0, firstVal);
        Assert.Equal(999.0, secondVal);
    }

    // ── Duck-typed foreach ────────────────────────────────────────────────────

    [Fact]
    public void Foreach_IteratesAllRows()
    {
        using var cursor = OpenCursor($"""
            <worksheet xmlns="{Ns}">
              <sheetData>
                <row r="1"><c r="A1"><v>1</v></c></row>
                <row r="2"><c r="A2"><v>2</v></c></row>
                <row r="3"><c r="A3"><v>3</v></c></row>
              </sheetData>
            </worksheet>
            """);

        var indices = new List<int>();
        foreach (var row in cursor)
        {
            indices.Add(row.RowIndex);
        }

        Assert.Equal([1, 2, 3], indices);
    }

    // ── Dispose ───────────────────────────────────────────────────────────────

    [Fact]
    public void Dispose_CalledTwice_DoesNotThrow()
    {
        var cursor = OpenCursor($"""
            <worksheet xmlns="{Ns}"><sheetData /></worksheet>
            """);

        cursor.Dispose();
        cursor.Dispose(); // second dispose must not throw
    }

    [Fact]
    public void MoveNext_AfterDispose_ReturnsFalse()
    {
        var cursor = OpenCursor($"""
            <worksheet xmlns="{Ns}">
              <sheetData><row r="1"><c r="A1"><v>1</v></c></row></sheetData>
            </worksheet>
            """);

        cursor.Dispose();
        Assert.False(cursor.MoveNext());
    }

    // ── Row shape and values ──────────────────────────────────────────────────

    [Fact]
    public void RowShape_SingleRow_WithColumnGap()
    {
        const string xml = $"""
            <worksheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main">
              <sheetData>
                <row r="1">
                  <c r="A1"><v>42</v></c>
                  <c r="C1"><v>3.14</v></c>
                </row>
              </sheetData>
            </worksheet>
            """;

        using var cursor = XlsxSheetScanner.OpenCursor(
            XmlStream(xml), SharedStringTable.Empty, StyleTable.Default, false,
            ReadMode.Values, ExcelRange.Unbounded);

        var rows = new List<ExcelRow>();
        foreach (var row in cursor)
        {
            // Clone to retain value after next MoveNext (for Assert below).
            rows.Add(row.ToSnapshot());
        }

        var single = Assert.Single(rows);
        Assert.Equal(1, single.RowIndex);
        Assert.Equal(1, single.StartColumn);
        Assert.Equal(3, single.CellCount);
        Assert.Equal(42.0, single.GetCell(1).AsNumber());
        Assert.True(single.GetCell(2).IsEmpty);
        Assert.Equal(3.14, single.GetCell(3).AsNumber());
    }

    [Fact]
    public void RowShape_MultipleRows_MultipleColumns()
    {
        const string xml = $"""
            <worksheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main">
              <sheetData>
                <row r="1">
                  <c r="A1"><v>1</v></c>
                  <c r="B1" t="s"><v>0</v></c>
                </row>
                <row r="2">
                  <c r="A2"><v>2</v></c>
                </row>
                <row r="5">
                  <c r="C5"><v>99</v></c>
                </row>
              </sheetData>
            </worksheet>
            """;
        var sst = SstBuilder.Make("hello");

        using var cursor = XlsxSheetScanner.OpenCursor(
            XmlStream(xml), sst, StyleTable.Default, false,
            ReadMode.Values, ExcelRange.Unbounded);

        var rows = new List<ExcelRow>();
        foreach (var row in cursor)
        {
            rows.Add(row.ToSnapshot());
        }

        Assert.Equal(3, rows.Count);

        Assert.Equal(1, rows[0].RowIndex);
        Assert.Equal(1, rows[0].StartColumn);
        Assert.Equal(2, rows[0].CellCount);
        Assert.Equal(1.0, rows[0].GetCell(1).AsNumber());
        Assert.Equal("hello", rows[0].GetCell(2).AsText());

        Assert.Equal(2, rows[1].RowIndex);
        Assert.Equal(1, rows[1].StartColumn);
        Assert.Equal(1, rows[1].CellCount);
        Assert.Equal(2.0, rows[1].GetCell(1).AsNumber());

        Assert.Equal(5, rows[2].RowIndex);
        Assert.Equal(3, rows[2].StartColumn);
        Assert.Equal(1, rows[2].CellCount);
        Assert.Equal(99.0, rows[2].GetCell(3).AsNumber());
    }

    // ── Async no-I/O loop regression ──────────────────────────────────────────

    // Regression: inline string text with a tag-name collision at span index 1 (the
    // 't' in "Item") made the no-I/O parse report NeedMoreData on every attempt, so
    // the TryParseNext/RefillAsync loop never terminated.
    [Fact]
    public async Task TryParseNext_InlineStringTagNameCollision_Terminates()
    {
        using var cursor = OpenCursor($"""
            <worksheet xmlns="{Ns}">
              <sheetData>
                <row r="1"><c r="A1" t="inlineStr"><is><t>Item_1</t></is></c></row>
                <row r="2"><c r="A2" t="inlineStr"><is><t>Item_2</t></is></c></row>
                <row r="3"><c r="A3" t="inlineStr"><is><t>Item_3</t></is></c></row>
              </sheetData>
            </worksheet>
            """);

        var rows = new List<ExcelRow>();
        int attempts = 0;
        while (attempts++ < 1000)
        {
            if (cursor.TryParseNext(out var row))
            {
                rows.Add(row.ToSnapshot());
                continue;
            }

            if (cursor.IsSheetDone) { break; }
            if (!await cursor.RefillAsync()) { break; }
        }

        Assert.True(attempts < 1000, "TryParseNext/RefillAsync loop did not terminate.");
        Assert.Equal(3, rows.Count);
        Assert.Equal("Item_1", rows[0].GetCell(1).AsText());
        Assert.Equal("Item_2", rows[1].GetCell(1).AsText());
        Assert.Equal("Item_3", rows[2].GetCell(1).AsText());
    }

    // Regression: a row larger than the 64 KiB ScanBuffer window (e.g. a long inline
    // string) rewound the buffer to start on every attempt, and RefillAsync couldn't
    // compact or read more space, so the loop never terminated.
    [Fact]
    public async Task TryParseNext_RowLargerThanBuffer_Terminates()
    {
        string hugeText = new string('A', 100_000);
        using var cursor = OpenCursor($"""
            <worksheet xmlns="{Ns}">
              <sheetData>
                <row r="1"><c r="A1" t="inlineStr"><is><t>{hugeText}</t></is></c></row>
                <row r="2"><c r="A2"><v>2</v></c></row>
              </sheetData>
            </worksheet>
            """);

        var rows = new List<ExcelRow>();
        int attempts = 0;
        while (attempts++ < 1000)
        {
            if (cursor.TryParseNext(out var row))
            {
                rows.Add(row.ToSnapshot());
                continue;
            }

            if (cursor.IsSheetDone) { break; }
            if (!await cursor.RefillAsync()) { break; }
        }

        Assert.True(attempts < 1000, "TryParseNext/RefillAsync loop did not terminate for a row larger than the buffer.");
        Assert.Equal(2, rows.Count);
        Assert.Equal(hugeText, rows[0].GetCell(1).AsText());
        Assert.Equal(2.0, rows[1].GetCell(1).AsNumber());
    }

    // ── Column projection ─────────────────────────────────────────────────────

    [Fact]
    public void MoveNext_WithProjection_SkipsValuesButKeepsCellPositions()
    {
        var sst = SstBuilder.Make("alpha", "beta");
        using var cursor = XlsxSheetScanner.OpenCursor(
            XmlStream($"""
                <worksheet xmlns="{Ns}">
                  <sheetData>
                    <row r="1">
                      <c r="A1" t="s"><v>0</v></c>
                      <c r="B1"><v>42</v></c>
                      <c r="C1" t="s"><v>1</v></c>
                    </row>
                  </sheetData>
                </worksheet>
                """),
            sst,
            StyleTable.Default,
            isDate1904: false,
            ReadMode.Values,
            ExcelRange.Unbounded,
            projection: new RowProjection([2]));

        Assert.True(cursor.MoveNext());
        var row = cursor.Current;
        // The window still spans A..C, but only the projected column carries a value.
        Assert.Equal(1, row.StartColumn);
        Assert.Equal(3, row.CellCount);
        Assert.True(row.GetCell(1).IsEmpty);
        Assert.Equal(42.0, row.GetCell(2).AsNumber());
        Assert.True(row.GetCell(3).IsEmpty);
    }

    [Fact]
    public void MoveNext_RowWithOnlyProjectedOutCells_IsStillYielded()
    {
        using var cursor = XlsxSheetScanner.OpenCursor(
            XmlStream($"""
                <worksheet xmlns="{Ns}">
                  <sheetData>
                    <row r="1"><c r="A1"><v>1</v></c></row>
                    <row r="2"><c r="A2"><v>2</v></c></row>
                  </sheetData>
                </worksheet>
                """),
            SharedStringTable.Empty,
            StyleTable.Default,
            isDate1904: false,
            ReadMode.Values,
            ExcelRange.Unbounded,
            projection: new RowProjection([5]));

        Assert.True(cursor.MoveNext());
        Assert.Equal(1, cursor.Current.RowIndex);
        Assert.True(cursor.Current.GetCell(1).IsEmpty);
        Assert.True(cursor.MoveNext());
        Assert.Equal(2, cursor.Current.RowIndex);
        Assert.False(cursor.MoveNext());
    }
}
