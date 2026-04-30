using XLSight.Internal.Readers.Xlsb;
using Xunit;

namespace XLSight.Tests.Readers.Xlsb;

public sealed class XlsbTableParserTests
{
    [Fact]
    public void Parse_ReadsTableNameRangeAndColumns()
    {
        using var stream = XlsbTestRecords.Stream(
            XlsbTestRecords.BeginList(2, 3, 10, 5, "Table1", "SalesTable"),
            XlsbTestRecords.BeginListColumn("Column1", "Region"),
            XlsbTestRecords.BeginListColumn("Column2", "Amount"));

        var table = XlsbTableParser.Parse(stream, "Sheet1");

        Assert.NotNull(table);
        Assert.Equal("SalesTable", table.Name);
        Assert.Equal("Sheet1", table.Sheet);
        Assert.Equal(new ExcelRange(new ExcelAddress(3, 2), new ExcelAddress(5, 10)), table.Range);
        Assert.Equal(["Region", "Amount"], table.ColumnNames);
    }

    [Fact]
    public void Parse_MalformedTable_ReturnsNull()
    {
        using var stream = XlsbTestRecords.Stream(
            XlsbTestRecords.Record(XlsbRecordType.BrtBeginList, [1, 2, 3]));

        Assert.Null(XlsbTableParser.Parse(stream, "Sheet1"));
    }
}
