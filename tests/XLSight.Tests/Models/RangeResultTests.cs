using Xunit;

namespace XLSight.Tests.Models;

public sealed class RangeResultTests
{
    [Fact]
    public void Rows_ReturnsCachedProjection()
    {
        var buffer = new[]
        {
            ExcelCellValue.FromNumber(1),
            ExcelCellValue.FromNumber(2),
            ExcelCellValue.FromNumber(3),
            ExcelCellValue.FromNumber(4),
        };

        var result = new RangeResult
        {
            Sheet = "Sheet1",
            StartRow = 1,
            StartColumn = 1,
            Width = 2,
            Height = 2,
            Cells = buffer,
        };

        Assert.Same(result.Rows, result.Rows);
    }

    [Fact]
    public void Cells_ExposesReadOnlyMemoryView()
    {
        var buffer = new[]
        {
            ExcelCellValue.FromNumber(1),
            ExcelCellValue.FromNumber(2),
        };

        var result = new RangeResult
        {
            Sheet = "Sheet1",
            StartRow = 1,
            StartColumn = 1,
            Width = 2,
            Height = 1,
            Cells = buffer,
        };

        Assert.Equal(2, result.Cells.Length);
        Assert.Equal(ExcelCellValue.FromNumber(1), result.Cells.Span[0]);

        buffer[0] = ExcelCellValue.FromNumber(99);

        Assert.Equal(ExcelCellValue.FromNumber(99), result.Cells.Span[0]);
    }
}
