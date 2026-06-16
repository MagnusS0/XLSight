using System.Buffers;
using System.Runtime.CompilerServices;

#pragma warning disable MA0048 // XlsbRecord is the iterator's borrowed payload view.

namespace XLSight.Internal.Readers.Xlsb;

internal sealed class XlsbRecordIterator : IDisposable
{
    // 32 KB is far below the 85 KB LOH threshold, protecting peak RSS and LOH.
    internal const int DefaultInputBufferSize = 32 * 1024;

    private readonly byte[] _input;
    private readonly Stream _stream;
    private byte[]? _oversize;
    private int _inputOffset;
    private int _inputLength;
    private bool _disposed;

    internal XlsbRecordIterator(Stream stream, int inputBufferSize = DefaultInputBufferSize)
    {
        _stream = stream;
        _input = ArrayPool<byte>.Shared.Rent(inputBufferSize);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal bool TryRead(out XlsbRecord record)
    {
        record = default;
        ThrowIfDisposed();

        if (!EnsureAvailable(6))
        {
            if (_inputLength == _inputOffset)
            {
                Dispose(); // Clean EOF: proactively return buffer to the pool
                return false;
            }
        }

        int first = ReadByte();
        int type = first & 0x7F;
        if ((first & 0x80) != 0)
        {
            type |= ReadByte() << 7;
        }

        int length = ReadVariableLength();

        // Fast path: payload fits in the input buffer contiguously.
        if (length <= _input.Length && EnsureAvailable(length))
        {
            record = new XlsbRecord(type, _input, _inputOffset, length);
            _inputOffset += length;
            return true;
        }

        return TryReadSlow(type, length, out record);
    }

    private bool TryReadSlow(int type, int length, out XlsbRecord record)
    {
        if (_oversize is null || _oversize.Length < length)
        {
            if (_oversize is not null)
            {
                ArrayPool<byte>.Shared.Return(_oversize);
            }
            _oversize = ArrayPool<byte>.Shared.Rent(length);
        }

        int alreadyBuffered = _inputLength - _inputOffset;
        _input.AsSpan(_inputOffset, alreadyBuffered).CopyTo(_oversize);
        _inputOffset = _inputLength;

        try
        {
            _stream.ReadExactly(_oversize.AsSpan(alreadyBuffered, length - alreadyBuffered));
        }
        catch (EndOfStreamException ex)
        {
            throw new MalformedWorkbookException("XLSB record payload is truncated.", ex);
        }

        record = new XlsbRecord(type, _oversize, 0, length);
        return true;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private bool EnsureAvailable(int required)
    {
        int available = _inputLength - _inputOffset;
        if (available >= required)
        {
            return true;
        }

        return EnsureAvailableSlow(required, available);
    }

    private bool EnsureAvailableSlow(int required, int available)
    {
        // Only shift if the remaining capacity from _inputOffset is too small.
        if (_input.Length - _inputOffset < required)
        {
            if (available > 0)
            {
                _input.AsSpan(_inputOffset, available).CopyTo(_input);
            }
            _inputOffset = 0;
            _inputLength = available;
        }

        while (_inputLength - _inputOffset < required)
        {
            int n = _stream.Read(_input, _inputLength, _input.Length - _inputLength);
            if (n == 0)
            {
                return _inputLength - _inputOffset >= required;
            }

            _inputLength += n;
        }

        return true;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private int ReadVariableLength()
    {
        int first = ReadByte();
        int value = first & 0x7F;
        if ((first & 0x80) == 0)
        {
            return value;
        }

        int shift = 7;
        for (int i = 1; i < 4; i++)
        {
            int b = ReadByte();
            value |= (b & 0x7F) << shift;
            if ((b & 0x80) == 0)
            {
                return value;
            }

            shift += 7;
        }

        throw new MalformedWorkbookException("XLSB record length header is invalid.");
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private int ReadByte()
    {
        if (_inputOffset >= _inputLength)
        {
            ThrowTruncatedException();
        }

        return _input[_inputOffset++];
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void ThrowTruncatedException()
    {
        throw new MalformedWorkbookException("XLSB record header is truncated.");
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        ArrayPool<byte>.Shared.Return(_input, clearArray: false);
        if (_oversize is not null)
        {
            ArrayPool<byte>.Shared.Return(_oversize, clearArray: false);
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void ThrowIfDisposed()
    {
        if (_disposed)
        {
            ThrowObjectDisposedException();
        }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private void ThrowObjectDisposedException()
    {
        throw new ObjectDisposedException(nameof(XlsbRecordIterator));
    }
}

internal readonly struct XlsbRecord(
    int type,
    byte[] buffer,
    int payloadStart,
    int payloadLength)
{
    internal int Type { get; } = type;
    internal byte[] Buffer { get; } = buffer;
    internal int PayloadStart { get; } = payloadStart;
    internal int PayloadLength { get; } = payloadLength;

    internal ReadOnlySpan<byte> Payload =>
        Buffer is not null ? Buffer.AsSpan(PayloadStart, PayloadLength) : default;
}

#pragma warning restore MA0048
