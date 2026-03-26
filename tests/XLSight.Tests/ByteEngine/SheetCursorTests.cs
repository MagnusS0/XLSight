using System.Text;
using Xunit;
using XLSight.ByteEngine;
using XLSight.Models;
using XLSight.Styles;

namespace XLSight.Tests.ByteEngine;

/// <summary>
/// Correctness tests for <see cref="SheetCursor"/>.
/// Covers cursor lifecycle, buffer-reuse contract, and parity with ScanRows.
/// </summary>
public sealed class SheetCursorTests
{
    private const string Ns = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";

    private static MemoryStream XmlStream(string xml)
        => new(Encoding.UTF8.GetBytes(xml));

    private static SheetCursor OpenCursor(string worksheetXml, string[]? sst = null, ExcelRange? range = null)
        => XlsxSheetScanner.OpenCursor(
            XmlStream(worksheetXml),
            sst ?? [],
            StyleTable.Default,
            isDate1904: false,
            ExcelReadMode.Values,
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

    // ── Parity with ScanRows ──────────────────────────────────────────────────

    [Fact]
    public void Parity_WithScanRows_SingleRow()
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

        var scanRows = XlsxSheetScanner.ScanRows(
            XmlStream(xml), [], StyleTable.Default, false,
            ExcelReadMode.Values, ExcelRange.Unbounded).ToList();

        using var cursor = XlsxSheetScanner.OpenCursor(
            XmlStream(xml), [], StyleTable.Default, false,
            ExcelReadMode.Values, ExcelRange.Unbounded);

        var cursorRows = new List<ExcelRow>();
        foreach (var row in cursor)
        {
            // Clone to retain value after next MoveNext (for Assert below).
            cursorRows.Add(row.CloneRow());
        }

        Assert.Equal(scanRows.Count, cursorRows.Count);
        for (int i = 0; i < scanRows.Count; i++)
        {
            Assert.Equal(scanRows[i].RowIndex, cursorRows[i].RowIndex);
            Assert.Equal(scanRows[i].StartColumn, cursorRows[i].StartColumn);
            Assert.Equal(scanRows[i].CellCount, cursorRows[i].CellCount);
        }
    }

    [Fact]
    public void Parity_WithScanRows_MultipleRows_MultipleColumns()
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
        string[] sst = ["hello"];

        var scanRows = XlsxSheetScanner.ScanRows(
            XmlStream(xml), sst, StyleTable.Default, false,
            ExcelReadMode.Values, ExcelRange.Unbounded).ToList();

        using var cursor = XlsxSheetScanner.OpenCursor(
            XmlStream(xml), sst, StyleTable.Default, false,
            ExcelReadMode.Values, ExcelRange.Unbounded);

        var cursorRows = new List<ExcelRow>();
        foreach (var row in cursor)
        {
            cursorRows.Add(row.CloneRow());
        }

        Assert.Equal(scanRows.Count, cursorRows.Count);
        for (int i = 0; i < scanRows.Count; i++)
        {
            var exp = scanRows[i];
            var act = cursorRows[i];
            Assert.Equal(exp.RowIndex, act.RowIndex);
            Assert.Equal(exp.StartColumn, act.StartColumn);
            Assert.Equal(exp.CellCount, act.CellCount);
            for (int col = exp.StartColumn; col < exp.StartColumn + exp.CellCount; col++)
            {
                Assert.Equal(exp.GetCell(col), act.GetCell(col));
            }
        }
    }
}
