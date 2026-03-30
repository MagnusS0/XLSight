using System.Runtime.InteropServices;

namespace XLSight.Internal.Readers.Xlsx;

[StructLayout(LayoutKind.Auto)]
internal readonly struct StartTagMatch(int start, int afterName, int endExclusive, bool isEmptyElement)
{
    internal int Start { get; } = start;
    internal int AfterName { get; } = afterName;
    internal int EndExclusive { get; } = endExclusive;
    internal bool IsEmptyElement { get; } = isEmptyElement;
}
