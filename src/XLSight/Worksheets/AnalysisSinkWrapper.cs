using XLSight.SharedStrings;
using XLSight.Models;
using XLSight.Models.Analysis;
using XLSight.Styles;

namespace XLSight.Worksheets;

internal sealed class AnalysisSinkWrapper : IWorksheetSink
{
    private AnalysisSink _inner;

    internal AnalysisSinkWrapper(SharedStringTable sharedStrings, StyleTable styles, bool isDate1904, ExcelReadMode mode)
    {
        _inner = new AnalysisSink(sharedStrings, styles, isDate1904, mode);
    }

    public void OnDimension(in ExcelRange dimension) => _inner.OnDimension(in dimension);
    public void OnRowStart(int rowIndex) => _inner.OnRowStart(rowIndex);
    public bool OnCell(in ParsedCell cell) => _inner.OnCell(in cell);
    public void OnMergeCell(in ExcelMergedRegion region) => _inner.OnMergeCell(in region);
    public void OnEnd() => _inner.OnEnd();

    internal ExcelSheetInfo Build(string name, int index, IReadOnlyList<ExcelTableInfo> tables)
        => _inner.Build(name, index, tables);
}
