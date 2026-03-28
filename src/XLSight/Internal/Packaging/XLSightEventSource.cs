using System.Diagnostics.Tracing;

namespace XLSight.Internal.Packaging;

[EventSource(Name = "XLSight")]
internal sealed class XLSightEventSource : EventSource
{
    internal static readonly XLSightEventSource Log = new();

    [Event(1, Level = EventLevel.Informational)]
    internal void WorkbookOpened(string source) => WriteEvent(1, source);

    [Event(2, Level = EventLevel.Informational)]
    internal void WorkbookDisposed() => WriteEvent(2);

    [Event(3, Level = EventLevel.Verbose)]
    internal void ReadRangeStart(string sheet, string range) => WriteEvent(3, sheet, range);

    [Event(4, Level = EventLevel.Verbose)]
    internal void ReadRangeStop() => WriteEvent(4);

    [Event(5, Level = EventLevel.Verbose)]
    internal void AnalyzeSheetStart(string sheet) => WriteEvent(5, sheet);

    [Event(6, Level = EventLevel.Verbose)]
    internal void AnalyzeSheetStop() => WriteEvent(6);

    [Event(7, Level = EventLevel.Verbose)]
    internal void ScanStart(string entryPath) => WriteEvent(7, entryPath);

    [Event(8, Level = EventLevel.Verbose)]
    internal void ScanStop() => WriteEvent(8);
}
