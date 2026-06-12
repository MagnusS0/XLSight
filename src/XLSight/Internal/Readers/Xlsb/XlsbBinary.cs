using System.Buffers.Binary;
using System.Runtime.CompilerServices;
using System.Text;

namespace XLSight.Internal.Readers.Xlsb;

internal static class XlsbBinary
{
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static int ReadInt32(ReadOnlySpan<byte> data, int offset) =>
        BinaryPrimitives.ReadInt32LittleEndian(data[offset..]);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static uint ReadUInt32(ReadOnlySpan<byte> data, int offset) =>
        BinaryPrimitives.ReadUInt32LittleEndian(data[offset..]);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static ushort ReadUInt16(ReadOnlySpan<byte> data, int offset) =>
        BinaryPrimitives.ReadUInt16LittleEndian(data[offset..]);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static double ReadDouble(ReadOnlySpan<byte> data, int offset) =>
        BitConverter.Int64BitsToDouble(BinaryPrimitives.ReadInt64LittleEndian(data[offset..]));

    internal static string ReadWideString(ReadOnlySpan<byte> data, ref int offset)
    {
        if (data.Length - offset < 4)
        {
            return string.Empty;
        }

        uint charCount = ReadUInt32(data, offset);
        offset += 4;
        int byteCount = checked((int)charCount * 2);
        if (byteCount == 0)
        {
            return string.Empty;
        }

        if (data.Length - offset < byteCount)
        {
            throw new MalformedWorkbookException("XLSB string payload is truncated.");
        }

        string value = Encoding.Unicode.GetString(data.Slice(offset, byteCount));
        offset += byteCount;
        return value;
    }

    internal static string ReadNullableWideString(ReadOnlySpan<byte> data, ref int offset)
    {
        if (data.Length - offset < 4)
        {
            return string.Empty;
        }

        uint charCount = ReadUInt32(data, offset);
        if (charCount == uint.MaxValue)
        {
            offset += 4;
            return string.Empty;
        }

        return ReadWideString(data, ref offset);
    }

    internal static string ReadRichStringText(ReadOnlySpan<byte> data)
    {
        if (data.IsEmpty)
        {
            return string.Empty;
        }

        int offset = 1;
        return ReadWideString(data, ref offset);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static int ReadRowIndex(ReadOnlySpan<byte> payload)
    {
        if (payload.Length < 4)
        {
            return 0;
        }

        return checked(ReadInt32(payload, 0) + 1);
    }

    internal static ExcelRange? TryReadRfx(ReadOnlySpan<byte> payload)
    {
        if (payload.Length < 16)
        {
            return null;
        }

        int firstRow = ReadInt32(payload, 0);
        int lastRow = ReadInt32(payload, 4);
        int firstColumn = ReadInt32(payload, 8);
        int lastColumn = ReadInt32(payload, 12);

        if (firstRow is < 0 or >= ExcelLimits.MaxRows ||
            lastRow is < 0 or >= ExcelLimits.MaxRows ||
            firstColumn is < 0 or >= ExcelLimits.MaxColumns ||
            lastColumn is < 0 or >= ExcelLimits.MaxColumns ||
            lastRow < firstRow ||
            lastColumn < firstColumn)
        {
            return null;
        }

        return new ExcelRange(
            new ExcelAddress(firstColumn + 1, firstRow + 1),
            new ExcelAddress(lastColumn + 1, lastRow + 1));
    }

    internal static string ReadRichStringTextWithOffset(ReadOnlySpan<byte> data, ref int offset)
    {
        if (data.Length - offset <= 0)
        {
            return string.Empty;
        }

        offset++;
        return ReadWideString(data, ref offset);
    }
}
