using System.Runtime.InteropServices;

namespace XLSight.Internal.Readers.Xlsb;

[StructLayout(LayoutKind.Auto)]
internal readonly ref struct XlsbRecord
{
    internal XlsbRecord(int type, ReadOnlySpan<byte> payload)
    {
        Type = type;
        Payload = payload;
    }

    internal int Type { get; }
    internal ReadOnlySpan<byte> Payload { get; }
}
