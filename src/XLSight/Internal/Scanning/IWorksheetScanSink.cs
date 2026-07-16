namespace XLSight.Internal.Scanning;

internal interface IWorksheetScanSink
{
    public void OnCell(int row, int column, in ExcelCellValue value, bool isFormula);
}
