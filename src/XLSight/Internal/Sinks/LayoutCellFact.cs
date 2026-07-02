using System.Runtime.InteropServices;

namespace XLSight.Internal.Sinks;

[StructLayout(LayoutKind.Auto)]
internal readonly record struct LayoutCellFact(
    int Row,
    int Column,
    LayoutKindMask KindMask,
    double NumericValue,
    bool HasNumericValue,
    bool IsHeaderLike,
    string? Text = null);
