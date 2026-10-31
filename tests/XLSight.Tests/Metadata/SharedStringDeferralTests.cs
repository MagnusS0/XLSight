using System.Reflection;
using System.Text;
using XLSight.Internal.Metadata;
using XLSight.Internal.Readers.Xlsx;
using Xunit;

namespace XLSight.Tests.Metadata;

/// <summary>
/// The shared-string table reads nothing (no scan window, no first inflate, no staging buffer,
/// no arena) until the first lookup.
/// </summary>
public sealed class SharedStringDeferralTests
{
    private const string Sst = """<sst uniqueCount="2"><si><t>Alpha</t></si><si><t>Beta</t></si></sst>""";

    [Fact]
    public void Parse_ReadsNothingUntilFirstLookup()
    {
        using var stream = new TrackingStream(Encoding.UTF8.GetBytes(Sst));
        using SharedStringTable table = SharedStringsByteParser.Parse(stream);

        Assert.Equal(0, stream.ReadCalls);
        Assert.False(table.HoldsParserState);

        Assert.Equal("Beta", table.GetString(1));
        Assert.True(stream.ReadCalls > 0);
        Assert.Equal("Alpha", table.GetString(0));
        Assert.Equal(2, table.Count);
        Assert.True(stream.IsDisposed); // completed table releases its stream
    }

    [Fact]
    public void Dispose_BeforeFirstLookup_ReleasesStreamWithoutReading()
    {
        var stream = new TrackingStream(Encoding.UTF8.GetBytes(Sst));
        SharedStringTable table = SharedStringsByteParser.Parse(stream);

        table.Dispose();

        Assert.True(stream.IsDisposed);
        Assert.Equal(0, stream.ReadCalls);
        Assert.Equal(string.Empty, table.GetString(0));
        Assert.Equal(0, table.GetCharCount(0));
    }

    [Fact]
    public void StartFailure_IsRethrownByEveryLookup()
    {
        var stream = new TrackingStream([], failReads: true);
        using SharedStringTable table = SharedStringsByteParser.Parse(stream);

        Assert.Throws<InvalidDataException>(() => table.GetString(0));
        Assert.Throws<InvalidDataException>(() => table.GetString(0));
        Assert.Equal(1, stream.ReadCalls);
    }

    [Fact]
    public void Workbook_NumericReadCell_DoesNotStartSharedStringParser()
    {
        string path = Path.Combine(AppContext.BaseDirectory, "TestData", "complex_workbook.xlsx");
        using var workbook = ExcelWorkbook.Open(path);

        Assert.Equal(30.0, workbook.ReadCell("Calculator", "C6").AsNumber());
        var engine = (XlsxWorkbookReader)typeof(ExcelWorkbook)
            .GetField("_engine", BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(workbook)!;
        SharedStringTable table = Assert.IsType<SharedStringTable>(engine.CreatedSharedStrings);
        Assert.False(table.HoldsParserState);

        Assert.Equal("Retirement Savings Calculator", workbook.ReadCell("Calculator", "B2").AsText());
        Assert.True(table.HoldsParserState);
    }

    private sealed class TrackingStream(byte[] data, bool failReads = false) : MemoryStream(data)
    {
        public int ReadCalls { get; private set; }

        public bool IsDisposed { get; private set; }

        public override int Read(byte[] buffer, int offset, int count)
        {
            ReadCalls++;
            if (failReads)
            {
                throw new InvalidDataException("corrupt shared-string part");
            }

            return base.Read(buffer, offset, count);
        }

        public override int Read(Span<byte> buffer)
        {
            byte[] chunk = new byte[buffer.Length];
            int read = Read(chunk, 0, chunk.Length);
            chunk.AsSpan(0, read).CopyTo(buffer);
            return read;
        }

        protected override void Dispose(bool disposing)
        {
            IsDisposed = true;
            base.Dispose(disposing);
        }
    }
}
