using XLSight.Internal.Readers;

namespace XLSight.Query.Internal;

internal interface IQueryScan
{
    public bool HeaderBound { get; }
    public bool SupportsProjection { get; }
    public ExcelRange? DataRangeAfterHeader(ExcelRange range);
    public RowProjection BuildProjection();
    public bool ProcessRow(in ExcelRow row);
}
