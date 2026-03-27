using System.Buffers;

namespace XLSight.ByteEngine;

/// <summary>
/// A 64 KB sliding window over a <see cref="Stream"/>, backed by a pooled byte array.
/// Sealed class (not ref struct) so it can be used across yield-return boundaries.
/// </summary>
internal sealed class ScanBuffer : IDisposable
{
    private const int BufferSize = 65536;

    private readonly Stream _source;
    private byte[] _buf;
    private int _start;   // index of first unconsumed byte
    private int _end;     // index one past the last valid byte
    private bool _streamDone;
    private bool _disposed;

    // ── Async no-I/O mode ──────────────────────────────────────────────────────
    // Set by SheetCursor.TryParseNext so that Refill() skips both compaction and the
    // stream read.  Because NO bytes are moved, the caller can restore _start cheaply
    // (Start / RewindTo) and then call RefillAsync which compacts and reads for real.
    internal bool NoIO;      // when true, Refill() is a no-op that returns false
    internal bool IOSkipped; // latched true by Refill() while NoIO; cleared by RefillAsync / TryParseNext

    internal ScanBuffer(Stream source)
    {
        _source = source;
        _buf = ArrayPool<byte>.Shared.Rent(BufferSize);
        _start = 0;
        _end = 0;
        _streamDone = false;
        _disposed = false;
        // Prime the buffer.
        Refill();
    }

    /// <summary>Current unconsumed window as a span.</summary>
    internal ReadOnlySpan<byte> Span => _buf.AsSpan(_start, _end - _start);

    internal bool CanReadMore => !_streamDone && (_start > 0 || _end < BufferSize);

    /// <summary>Current start index — saved by <see cref="SheetCursor.TryParseNext"/> for rollback.</summary>
    internal int Start => _start;

    /// <summary>
    /// Restores the start index to a previously saved value.
    /// Only valid when <see cref="NoIO"/> was true during the parse attempt (no compaction occurred).
    /// </summary>
    internal void RewindTo(int savedStart) => _start = savedStart;

    /// <summary>
    /// Resets the buffer pointers and refills from the underlying stream's current position.
    /// Used after an external seek on the stream (e.g. when a seek hint repositions it).
    /// </summary>
    internal void Reset()
    {
        _start = 0;
        _end = 0;
        _streamDone = false;
        Refill();
    }

    /// <summary>
    /// Marks <paramref name="n"/> bytes as consumed (advances the start pointer).
    /// Does not shift the buffer — shifting only happens in <see cref="Refill"/>.
    /// </summary>
    internal void Advance(int n)
    {
        _start += n;
    }

    /// <summary>
    /// Shifts unconsumed bytes to the front of the buffer and reads more from the stream.
    /// Returns <see langword="false"/> when the stream is exhausted and no bytes remain.
    /// <para>
    /// When <see cref="NoIO"/> is <see langword="true"/> this method is a no-op: it skips
    /// both compaction and the stream read, sets <see cref="IOSkipped"/>, and returns
    /// <see langword="false"/>.  The caller (<see cref="SheetCursor.TryParseNext"/>) can
    /// then rewind via <see cref="RewindTo"/> and issue a real async refill.
    /// </para>
    /// </summary>
    internal bool Refill()
    {
        // In no-I/O mode skip everything so the buffer layout is unchanged and the
        // caller can roll back cheaply before doing a true async refill.
        if (NoIO)
        {
            IOSkipped = true;
            return false;
        }

        // Shift unconsumed bytes to the front.
        int remaining = _end - _start;
        if (_start > 0)
        {
            if (remaining > 0)
            {
                _buf.AsSpan(_start, remaining).CopyTo(_buf.AsSpan(0, remaining));
            }

            _start = 0;
            _end = remaining;
        }

        // Fill the rest of the buffer from the stream.
        if (!_streamDone)
        {
            int space = BufferSize - _end;
            if (space > 0)
            {
                int bytesRead = _source.Read(_buf, _end, space);
                if (bytesRead == 0)
                {
                    _streamDone = true;
                }
                else
                {
                    _end += bytesRead;
                }
            }
        }

        return _end > 0;
    }

    /// <summary>
    /// Compacts unconsumed bytes to the front of the buffer, then reads more data from the
    /// stream asynchronously.  Returns <see langword="false"/> when the stream is exhausted
    /// and no bytes remain in the buffer.
    /// </summary>
    /// <remarks>
    /// Because <see cref="NoIO"/> mode skips compaction, the caller first restores
    /// <see cref="Start"/> via <see cref="RewindTo"/> before calling this method so that
    /// the compaction step here shifts from the correct position.
    /// </remarks>
    internal async ValueTask<bool> RefillAsync(CancellationToken ct = default)
    {
        IOSkipped = false;

        // Compact unconsumed bytes to the front.
        int remaining = _end - _start;
        if (_start > 0 && remaining > 0)
        {
            _buf.AsSpan(_start, remaining).CopyTo(_buf);
        }
        _start = 0;
        _end = remaining;

        if (!_streamDone)
        {
            int space = _buf.Length - _end;
            if (space > 0)
            {
                int bytesRead = await _source.ReadAsync(_buf.AsMemory(_end, space), ct).ConfigureAwait(false);
                if (bytesRead == 0)
                {
                    _streamDone = true;
                }
                else
                {
                    _end += bytesRead;
                }
            }
        }

        return _end > _start;
    }

    /// <summary>True when the stream is exhausted and <see cref="Span"/> is empty.</summary>
    internal bool IsExhausted => _streamDone && _start >= _end;

    /// <inheritdoc/>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        ArrayPool<byte>.Shared.Return(_buf);
        _buf = [];
    }
}
