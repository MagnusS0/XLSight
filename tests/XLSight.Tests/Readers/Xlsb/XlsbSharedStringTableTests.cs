using XLSight.Internal.Readers.Xlsb;
using Xunit;

namespace XLSight.Tests.Readers.Xlsb;

public sealed class XlsbSharedStringTableTests
{
    [Fact]
    public void GetString_ParsesOnlyThroughRequestedIndex()
    {
        using var stream = XlsbTestRecords.Stream(
            XlsbTestRecords.SharedStringItem("first"),
            [0x80]);
        using XlsbSharedStringTable table = XlsbSharedStringsParser.Parse(stream);

        Assert.Equal("first", table.GetString(0));
    }

    [Fact]
    public void GetString_CanContinueParsingLaterIndexes()
    {
        using var stream = XlsbTestRecords.Stream(
            XlsbTestRecords.SharedStringItem("first"),
            XlsbTestRecords.SharedStringItem("second"));
        using XlsbSharedStringTable table = XlsbSharedStringsParser.Parse(stream);

        Assert.Equal("second", table.GetString(1));
        Assert.Equal("first", table.GetString(0));
    }
}
