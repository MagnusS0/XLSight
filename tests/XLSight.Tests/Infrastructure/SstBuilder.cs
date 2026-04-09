using System.Buffers;
using System.Text;
using XLSight.Internal.Metadata;

namespace XLSight.Tests.Infrastructure;

internal static class SstBuilder
{
    internal static SharedStringTable Make(params string[] strings)
    {
        if (strings.Length == 0) { return SharedStringTable.Empty; }

        var arena = new ArrayBufferWriter<byte>(strings.Length * 16);
        var info = new long[strings.Length];

        for (int i = 0; i < strings.Length; i++)
        {
            int start = arena.WrittenCount;
            int written = Encoding.UTF8.GetBytes(
                strings[i],
                arena.GetSpan(Encoding.UTF8.GetMaxByteCount(strings[i].Length)));
            arena.Advance(written);
            // Single-chunk layout: chunkIdx = 0, so globalOffset = (0 << 16) | start = start.
            info[i] = ((long)start << 32) | (uint)written;
        }

        // Wrap in single-chunk arrays matching SharedStringTable's chunked constructor.
        return new SharedStringTable([arena.WrittenSpan.ToArray()], [info], strings.Length);
    }
}
