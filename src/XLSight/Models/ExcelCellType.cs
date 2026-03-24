namespace XLSight.Models;

public enum ExcelCellType : byte
{
    Empty = 0,
    Text = 1,
    Number = 2,
    Date = 3,
    Boolean = 4,
    Error = 5,
    Formula = 6,  // only populated in ExcelReadMode.Formulas
}
