using System.Text;
using XLSight.Analysis;
using XLSight.Internal.Metadata;
using XLSight.Internal.Readers.Xlsx;
using XLSight.Internal.Sinks;
using XLSight.Tests.Infrastructure;
using Xunit;

namespace XLSight.Tests.Readers.Xlsx;

public sealed class SharedStringCellDecodeTests
{
    public static TheoryData<string, int, string?> SharedStringValues => new()
    {
        { "0", 0, "First" },
        { "1", 1, "第二" },
        { "+1", 1, "第二" },
        { "01", 1, "第二" },
        { "1suffix", 1, "第二" },
        { "1 ", 1, "第二" },
        { "-1", -1, "" },
        { "-2", -2, "" },
        { "2", 2, "" },
        { "2147483647", int.MaxValue, "" },
        { "", -1, null },
        { "invalid", -1, null },
        { " 1", -1, null },
        { "2147483648", -1, null },
        { "-2147483649", -1, null },
    };

    [Theory]
    [MemberData(nameof(SharedStringValues))]
    public void Cursor_SharedStringValue_PreservesTextAndIdentity(string bytes, int rawIndex, string? text)
    {
        using var sharedStrings = SstBuilder.Make("First", "第二");
        using var stream = Worksheet(bytes);
        using var cursor = XlsxSheetScanner.OpenCursor(
            stream, sharedStrings, StyleTable.Default, false, ReadMode.Values, ExcelRange.Unbounded);

        Assert.True(cursor.MoveNext());
        AssertValue(cursor.Current.GetCell(1), rawIndex, text);
        Assert.False(cursor.MoveNext());
    }

    [Theory]
    [MemberData(nameof(SharedStringValues))]
    public void Sink_SharedStringValue_PreservesRawIndexWithAndWithoutDecoding(
        string bytes, int rawIndex, string? text)
    {
        foreach (bool decode in new[] { false, true })
        {
            foreach (bool trackFormulas in new[] { false, true })
            {
                using var sharedStrings = SstBuilder.Make("First", "第二");
                using var stream = Worksheet(bytes, trackFormulas);
                var sink = new SharedStringSink(decode, trackFormulas);

                XlsxSheetScanner.ScanSheet(
                    stream, sharedStrings, StyleTable.Default, false,
                    ReadMode.Values, ExcelRange.Unbounded, ref sink, ct: TestContext.Current.CancellationToken);

                Assert.Equal(1, sink.CellCount);
                Assert.Equal(rawIndex, sink.RawIndex);
                Assert.Equal(trackFormulas ? 1 : 0, sink.FormulaCount);
                AssertValue(sink.Value, rawIndex, decode ? text : null);
            }
        }
    }

    private static void AssertValue(ExcelCellValue value, int rawIndex, string? text)
    {
        if (text is null)
        {
            Assert.True(value.IsEmpty);
            Assert.False(value.TryGetSharedStringId(out _));
            return;
        }

        Assert.Equal(CellType.Text, value.CellType);
        Assert.Equal(text, value.AsText());
        Assert.Equal(rawIndex >= 0, value.TryGetSharedStringId(out int id));
        Assert.Equal(rawIndex >= 0 ? rawIndex : -1, id);
    }

    private static MemoryStream Worksheet(string value, bool withFormula = false)
        => new(Encoding.UTF8.GetBytes($"""
            <worksheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main">
              <sheetData><row r="1"><c r="A1" t="s">{(withFormula ? "<f>\"text\"</f>" : "")}<v>{value}</v></c></row></sheetData>
            </worksheet>
            """));

    private struct SharedStringSink(bool decode, bool trackFormulas) : IByteSheetSink
    {
        public bool NeedsDecodedValue => decode;
        public bool TracksFormulas => trackFormulas;
        public bool TracksFormulaReferences => false;
        internal ExcelCellValue Value;
        internal int RawIndex;
        internal int CellCount;
        internal int FormulaCount;

        public bool OnCell(int column, CellDataKind kind, int styleIdx, ExcelCellValue value, int rawIndex)
        {
            Value = value;
            RawIndex = rawIndex;
            CellCount++;
            return true;
        }

        public void OnFormula(int column, bool isArray) => FormulaCount++;
        public void OnDimension(in ExcelRange dimension) { }
        public void OnRowStart(int rowIndex) { }
        public void OnFormulaReference(in FormulaReference reference) { }
        public void OnSharedFormulaDefinition(int sharedIndex) { }
        public void OnSharedFormulaReference(int sharedIndex) { }
        public void OnMergeCell(in MergedRegion region) { }
        public void OnConditionalFormatting() { }
        public void OnDataValidation(DataValidationInfo? validation) { }
        public void OnHyperlink() { }
        public void OnEnd() { }
    }
}
