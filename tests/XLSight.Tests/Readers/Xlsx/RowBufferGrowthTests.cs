using System.Text;
using XLSight.Internal.Metadata;
using XLSight.Internal.Readers;
using XLSight.Internal.Readers.Xlsx;
using Xunit;

namespace XLSight.Tests.Readers.Xlsx;

public sealed class RowBufferGrowthTests
{
    [Fact]
    public void MoveNext_GrowsAcrossSparseColumns_AndClearsValuesBetweenRows()
    {
        using var stream = XmlStream("""
            <row r="1"><c r="A1" t="inlineStr"><is><t>first</t></is></c><c r="IW1" t="inlineStr"><is><t>grown</t></is></c><c r="XFD1"><v>16384</v></c></row>
            <row r="2"><c r="A2"><v>2</v></c><c r="XFD2"><v>3</v></c></row>
            <row r="3"><c r="C3"><v>4</v></c></row>
            <row r="4"><c r="A4"><v>5</v></c><c r="XFD4"><v>6</v></c></row>
            """);
        using var cursor = OpenCursor(stream);

        Assert.True(cursor.MoveNext());
        var snapshot = cursor.Current.ToSnapshot();
        Assert.Equal(16384, snapshot.CellCount);
        Assert.Equal("first", snapshot.GetCell(1).AsText());
        Assert.Equal("grown", snapshot.GetCell(257).AsText());
        Assert.Equal(16384, snapshot.GetCell(16384).AsNumber());
        AssertEmptyGaps(snapshot, 1, 257, 16384);

        Assert.True(cursor.MoveNext());
        Assert.Equal(2, cursor.Current.GetCell(1).AsNumber());
        Assert.Equal(3, cursor.Current.GetCell(16384).AsNumber());
        AssertEmptyGaps(cursor.Current, 1, 16384);

        Assert.True(cursor.MoveNext());
        Assert.Equal(3, cursor.Current.StartColumn);
        Assert.Equal(1, cursor.Current.CellCount);
        Assert.Equal(4, cursor.Current.GetCell(3).AsNumber());

        Assert.True(cursor.MoveNext());
        Assert.Equal(5, cursor.Current.GetCell(1).AsNumber());
        Assert.Equal(6, cursor.Current.GetCell(16384).AsNumber());
        AssertEmptyGaps(cursor.Current, 1, 16384);
        Assert.False(cursor.MoveNext());
        Assert.Equal("grown", snapshot.GetCell(257).AsText());
    }

    [Fact]
    public void MoveNext_GrowsWithinRange_WithProjectionAndNonzeroColumnOffset()
    {
        using var stream = XmlStream("""
            <row r="1"><c r="A1"><v>1</v></c><c r="H1"><v>8</v></c><c r="ZZ1" t="inlineStr"><is><t>last</t></is></c><c r="XFD1"><v>16384</v></c></row>
            """);
        using var cursor = OpenCursor(stream, ExcelRange.Parse("H1:ZZ1"), new RowProjection([702]));

        Assert.True(cursor.MoveNext());
        Assert.Equal(8, cursor.Current.StartColumn);
        Assert.Equal(695, cursor.Current.CellCount);
        Assert.Equal("last", cursor.Current.GetCell(702).AsText());
        AssertEmptyGaps(cursor.Current, 702);
        Assert.False(cursor.MoveNext());
    }

    [Fact]
    public async Task TryParseNext_GrowthBeforeBufferBoundary_RetriesWithoutLeakingPartialRow()
    {
        string longText = new('中', 32767);
        using var stream = XmlStream($"""
            <row r="1"><c r="A1" t="inlineStr"><is><t>first</t></is></c><c r="IW1" t="inlineStr"><is><t>{longText}</t></is></c><c r="XFD1"><v>1</v></c></row>
            <row r="2"><c r="A2"><v>2</v></c><c r="XFD2"><v>3</v></c></row>
            """);
        using var cursor = OpenCursor(stream);
        var rows = new List<ExcelRow>();
        int attempts = 0;
        while (attempts++ < 20)
        {
            if (cursor.TryParseNext(out var row))
            {
                rows.Add(row.ToSnapshot());
                continue;
            }

            if (cursor.IsSheetDone) { break; }
            if (!await cursor.RefillAsync(TestContext.Current.CancellationToken)) { break; }
        }

        Assert.True(attempts < 20, "The parse/refill loop must make progress.");
        Assert.Equal(2, rows.Count);
        Assert.Equal("first", rows[0].GetCell(1).AsText());
        Assert.Equal(longText, rows[0].GetCell(257).AsText());
        Assert.Equal(1, rows[0].GetCell(16384).AsNumber());
        AssertEmptyGaps(rows[0], 1, 257, 16384);
        Assert.Equal(2, rows[1].RowIndex);
        AssertEmptyGaps(rows[1], 1, 16384);
    }

    private static void AssertEmptyGaps(ExcelRow row, params int[] occupiedColumns)
    {
        for (int column = row.StartColumn; column < row.StartColumn + row.CellCount; column++)
        {
            if (!occupiedColumns.Contains(column)) { Assert.True(row.GetCell(column).IsEmpty); }
        }
    }

    private static MemoryStream XmlStream(string rows) =>
        new(Encoding.UTF8.GetBytes($"<worksheet><sheetData>{rows}</sheetData></worksheet>"));

    private static SheetCursor OpenCursor(Stream stream, ExcelRange? range = null, RowProjection? projection = null) =>
        XlsxSheetScanner.OpenCursor(stream, SharedStringTable.Empty, StyleTable.Default, false,
            ReadMode.Values, range ?? ExcelRange.Unbounded, projection: projection);
}
