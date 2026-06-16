using XLSight.Internal.Readers.Xlsb;
using Xunit;

namespace XLSight.Tests.Readers.Xlsb;

public sealed class XlsbSharedStringTableTests
{
    [Fact]
    public void GetString_ParsesOnlyThroughRequestedIndex()
    {
        using var stream = XlsbTestRecords.Stream(
            XlsbTestRecords.BeginSst(total: 2, unique: 2),
            XlsbTestRecords.SharedStringItem("first"),
            [0x80]);
        using XlsbSharedStringTable table = XlsbSharedStringTable.Parse(stream);

        Assert.Equal("first", table.GetString(0));
    }

    [Fact]
    public void GetString_CanContinueParsingLaterIndexes()
    {
        using var stream = XlsbTestRecords.Stream(
            XlsbTestRecords.BeginSst(total: 2, unique: 2),
            XlsbTestRecords.SharedStringItem("first"),
            XlsbTestRecords.SharedStringItem("second"));
        using XlsbSharedStringTable table = XlsbSharedStringTable.Parse(stream);

        Assert.Equal("second", table.GetString(1));
        Assert.Equal("first", table.GetString(0));
    }

    [Fact]
    public void GetString_DoesNotAllocateAllDeclaredStringsForEarlyIndex()
    {
        using var stream = XlsbTestRecords.Stream(
            XlsbTestRecords.BeginSst(total: 1_000_000, unique: 1_000_000),
            XlsbTestRecords.SharedStringItem("first"),
            [0x80]);
        using XlsbSharedStringTable table = XlsbSharedStringTable.Parse(stream);

        Assert.Equal(0, table.AllocatedChunkCount);
        Assert.Equal("first", table.GetString(0));
        Assert.Equal(1, table.AllocatedChunkCount);
    }

    [Fact]
    public void GetString_GrowsChunksWhenUniqueCountIsUnderreported()
    {
        const int targetIndex = 512;
        var records = new List<byte[]>(targetIndex + 2)
        {
            XlsbTestRecords.BeginSst(total: targetIndex + 1, unique: 1),
        };

        for (int i = 0; i < targetIndex; i++)
        {
            records.Add(XlsbTestRecords.SharedStringItem("skip"));
        }

        records.Add(XlsbTestRecords.SharedStringItem("target"));

        using var stream = XlsbTestRecords.Stream([.. records]);
        using XlsbSharedStringTable table = XlsbSharedStringTable.Parse(stream);

        Assert.Equal("target", table.GetString(targetIndex));
        Assert.Equal(2, table.AllocatedChunkCount);
    }
}
