using XLSight.Analysis;
using Xunit;

namespace XLSight.Tests.Analysis;

public sealed class TableInfoTests
{
    [Fact]
    public void Constructor_SetsNameProperty()
    {
        var table = new TableInfo
        {
            Name = "MyTable",
            Sheet = "Sheet1",
            Range = new ExcelRange(new ExcelAddress(1, 1), new ExcelAddress(5, 10)),
            ColumnNames = ["Col1", "Col2"],
        };
        Assert.Equal("MyTable", table.Name);
    }

    [Fact]
    public void Constructor_SetsSheetProperty()
    {
        var table = new TableInfo
        {
            Name = "T",
            Sheet = "DataSheet",
            Range = new ExcelRange(new ExcelAddress(1, 1), new ExcelAddress(3, 5)),
            ColumnNames = [],
        };
        Assert.Equal("DataSheet", table.Sheet);
    }

    [Fact]
    public void Constructor_SetsRangeProperty()
    {
        var range = new ExcelRange(new ExcelAddress(2, 3), new ExcelAddress(6, 10));
        var table = new TableInfo
        {
            Name = "T",
            Sheet = "S",
            Range = range,
            ColumnNames = [],
        };
        Assert.Equal(range, table.Range);
    }

    [Fact]
    public void Constructor_SetsColumnNamesProperty()
    {
        var cols = new[] { "ID", "Name", "Value" };
        var table = new TableInfo
        {
            Name = "T",
            Sheet = "S",
            Range = new ExcelRange(new ExcelAddress(1, 1), new ExcelAddress(3, 5)),
            ColumnNames = cols,
        };
        Assert.Equal(cols, table.ColumnNames);
    }

    [Fact]
    public void Constructor_EmptyColumnNames_Allowed()
    {
        var table = new TableInfo
        {
            Name = "EmptyTable",
            Sheet = "Sheet1",
            Range = new ExcelRange(new ExcelAddress(1, 1), new ExcelAddress(1, 1)),
            ColumnNames = [],
        };
        Assert.Empty(table.ColumnNames);
    }

    [Fact]
    public void Range_WidthAndHeight_AreComputedCorrectly()
    {
        var table = new TableInfo
        {
            Name = "T",
            Sheet = "S",
            Range = new ExcelRange(new ExcelAddress(1, 1), new ExcelAddress(5, 10)),
            ColumnNames = [],
        };
        Assert.Equal(5, table.Range.Width);
        Assert.Equal(10, table.Range.Height);
    }
}
