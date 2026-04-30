using System.Buffers;

namespace XLSight.Internal.Readers.Xlsb;

internal sealed class XlsbRecordIterator : IDisposable
{
    private readonly Stream _stream;
    private byte[] _payload;
    private bool _disposed;

    internal XlsbRecordIterator(Stream stream)
    {
        _stream = stream;
        _payload = ArrayPool<byte>.Shared.Rent(4096);
    }

    internal bool TryRead(out XlsbRecord record)
    {
        record = default;
        ThrowIfDisposed();

        int first = _stream.ReadByte();
        if (first < 0)
        {
            return false;
        }

        int type = first & 0x7F;
        if ((first & 0x80) != 0)
        {
            int second = _stream.ReadByte();
            if (second < 0)
            {
                throw new MalformedWorkbookException("XLSB record type header is truncated.");
            }

            type |= second << 7;
        }

        int length = ReadVariableLength();
        EnsurePayload(length);
        ReadExactly(_payload.AsSpan(0, length));
        record = new XlsbRecord(type, _payload.AsSpan(0, length));
        return true;
    }

    private int ReadVariableLength()
    {
        int value = 0;
        int shift = 0;

        for (int i = 0; i < 4; i++)
        {
            int b = _stream.ReadByte();
            if (b < 0)
            {
                throw new MalformedWorkbookException("XLSB record length header is truncated.");
            }

            value |= (b & 0x7F) << shift;
            if ((b & 0x80) == 0)
            {
                return value;
            }

            shift += 7;
        }

        throw new MalformedWorkbookException("XLSB record length header is invalid.");
    }

    private void EnsurePayload(int length)
    {
        if (length <= _payload.Length)
        {
            return;
        }

        ArrayPool<byte>.Shared.Return(_payload, clearArray: false);
        _payload = ArrayPool<byte>.Shared.Rent(length);
    }

    private void ReadExactly(Span<byte> destination)
    {
        try
        {
            _stream.ReadExactly(destination);
        }
        catch (EndOfStreamException)
        {
            throw new MalformedWorkbookException("XLSB record payload is truncated.");
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        ArrayPool<byte>.Shared.Return(_payload, clearArray: false);
    }

    private void ThrowIfDisposed()
    {
        if (_disposed)
        {
            ObjectDisposedException.ThrowIf(true, nameof(XlsbRecordIterator));
        }
    }
}
