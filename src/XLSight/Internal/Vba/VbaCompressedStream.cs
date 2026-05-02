using System.Buffers.Binary;

namespace XLSight.Internal.Vba;

internal static class VbaCompressedStream
{
    private const int ChunkSize = 4096;
    private const int MaxDecompressedBytes = 32 * 1024 * 1024;

    public static byte[] Decompress(ReadOnlySpan<byte> compressed)
    {
        if (compressed.IsEmpty || compressed[0] != 0x01)
        {
            throw new VbaProjectParseException("Invalid VBA compressed stream: signature byte 0x01 was not found.");
        }

        var output = new List<byte>(Math.Min(compressed.Length * 2, ChunkSize));
        var offset = 1;

        while (offset < compressed.Length)
        {
            EnsureAvailable(compressed, offset, 2, "chunk header");
            var chunkStart = offset;
            var header = BinaryPrimitives.ReadUInt16LittleEndian(compressed.Slice(offset, 2));
            offset += 2;

            var compressedChunkSize = header & 0x0FFF;
            var chunkSignature = (header & 0x7000) >> 12;
            var isCompressed = (header & 0x8000) != 0;
            if (chunkSignature != 0b011)
            {
                throw new VbaProjectParseException($"Invalid VBA compressed stream: chunk signature {chunkSignature} is unsupported.");
            }

            int chunkEnd = GetChunkEnd(compressed.Length, chunkStart, compressedChunkSize);

            var decompressedChunkStart = output.Count;
            if (!isCompressed)
            {
                AppendUncompressedChunk(compressed, output, ref offset, compressedChunkSize, chunkEnd);
                continue;
            }

            while (offset < chunkEnd)
            {
                EnsureChunkAvailable(compressed, offset, 1, chunkEnd, "flag byte");
                var flags = compressed[offset++];
                for (var bit = 0; bit < 8 && offset < chunkEnd; bit++)
                {
                    if ((flags & (1 << bit)) == 0)
                    {
                        EnsureChunkAvailable(compressed, offset, 1, chunkEnd, "literal");
                        Append(output, compressed.Slice(offset, 1));
                        offset++;
                        continue;
                    }

                    EnsureChunkAvailable(compressed, offset, 2, chunkEnd, "copy token");
                    var token = BinaryPrimitives.ReadUInt16LittleEndian(compressed.Slice(offset, 2));
                    offset += 2;
                    CopyToken(output, decompressedChunkStart, token);
                }
            }
        }

        return [.. output];
    }

    private static int GetChunkEnd(int compressedLength, int chunkStart, int compressedChunkSize)
    {
        var chunkEnd = chunkStart + compressedChunkSize + 3;
        if (chunkEnd < chunkStart || chunkEnd > compressedLength)
        {
            throw new VbaProjectParseException("Invalid VBA compressed stream: truncated chunk.");
        }

        return chunkEnd;
    }

    private static void AppendUncompressedChunk(
        ReadOnlySpan<byte> compressed,
        List<byte> output,
        ref int offset,
        int compressedChunkSize,
        int chunkEnd)
    {
        var declaredSize = compressedChunkSize + 1;
        if (declaredSize > ChunkSize)
        {
            throw new VbaProjectParseException("Invalid VBA compressed stream: uncompressed chunk exceeds supported bounds.");
        }

        EnsureChunkAvailable(compressed, offset, declaredSize, chunkEnd, "uncompressed chunk");
        Append(output, compressed.Slice(offset, declaredSize));
        offset += declaredSize;
    }

    private static void CopyToken(List<byte> output, int chunkStart, ushort token)
    {
        var decompressedBytesInChunk = output.Count - chunkStart;
        var bitCount = 4;
        while (bitCount < 12 && (1 << bitCount) < decompressedBytesInChunk)
        {
            bitCount++;
        }

        var lengthMask = 0xFFFF >> bitCount;
        var length = (token & lengthMask) + 3;
        var offset = ((token & ~lengthMask) >> (16 - bitCount)) + 1;
        if (offset <= 0 || offset > output.Count - chunkStart)
        {
            throw new VbaProjectParseException("Invalid VBA compressed stream: copy token points before the current chunk.");
        }

        for (var i = 0; i < length; i++)
        {
            var value = output[output.Count - offset];
            output.Add(value);
            if (output.Count > MaxDecompressedBytes)
            {
                throw new VbaProjectParseException("Invalid VBA compressed stream: decompressed output exceeds supported bounds.");
            }
        }
    }

    private static void Append(List<byte> output, ReadOnlySpan<byte> bytes)
    {
        if (output.Count + bytes.Length > MaxDecompressedBytes)
        {
            throw new VbaProjectParseException("Invalid VBA compressed stream: decompressed output exceeds supported bounds.");
        }

        output.AddRange(bytes);
    }

    private static void EnsureAvailable(ReadOnlySpan<byte> bytes, int offset, int length, string section)
    {
        if (offset < 0 || length < 0 || offset + length > bytes.Length)
        {
            throw new VbaProjectParseException($"Invalid VBA compressed stream: truncated {section}.");
        }
    }

    private static void EnsureChunkAvailable(ReadOnlySpan<byte> bytes, int offset, int length, int chunkEnd, string section)
    {
        if (offset < 0 || length < 0 || offset + length > chunkEnd || offset + length > bytes.Length)
        {
            throw new VbaProjectParseException($"Invalid VBA compressed stream: truncated {section}.");
        }
    }
}
