using XLSight.Models;
using XLSight.Models.Analysis;

namespace XLSight.Worksheets;

internal interface IWorksheetSink
{
    /// <summary>Called when a &lt;dimension&gt; element is found with the worksheet's used range.</summary>
    public void OnDimension(in ExcelRange dimension);

    /// <summary>Called at the start of each &lt;row&gt; element.</summary>
    public void OnRowStart(int rowIndex);

    /// <summary>
    /// Called for each cell. Return <see langword="false"/> to stop scanning (early termination).
    /// </summary>
    public bool OnCell(in ParsedCell cell);

    /// <summary>Called for each &lt;mergeCell&gt; element.</summary>
    public void OnMergeCell(in ExcelMergedRegion region);

    /// <summary>Called when scanning completes normally (not on early termination).</summary>
    public void OnEnd();
}
