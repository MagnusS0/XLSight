using System.Buffers;

namespace XLSight.Internal.Readers.Xlsx;

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
    // Used by TryWithoutIO: Refill() skips both compaction and the stream read while
    // _noIO is true.  Because NO bytes are moved, _start can be cheaply restored
    // before RefillAsync compacts and reads for real.
    private bool _noIO;
    private bool _ioSkipped;

    /// <summary>
    /// True when the most recent <see cref="TryWithoutIO"/> call was interrupted by a
    /// needed refill (buffer was rewound). False when the parse completed without I/O.
    /// </summary>
    internal bool LastParseNeededIO => _ioSkipped;

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

    internal bool CanReadMore => !_streamDone && (_start > 0 || _end < _buf.Length);

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
    /// When inside a <see cref="TryWithoutIO"/> call this method is a no-op: it skips
    /// both compaction and the stream read, sets the skipped flag, and returns
    /// <see langword="false"/> so the outer async loop can do a real refill.
    /// </para>
    /// </summary>
    internal bool Refill()
    {
        // In no-I/O mode skip everything so the buffer layout is unchanged and the
        // caller can roll back cheaply before doing a true async refill.
        if (_noIO)
        {
            _ioSkipped = true;
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

        // Read once from the stream — accept partial fills so the parser can process
        // rows immediately instead of blocking until the full 64 KB buffer is filled.
        // (DeflateStream/zlib-ng returns data in variable-size chunks.)
        if (!_streamDone)
        {
            int space = _buf.Length - _end;
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
    /// Because <see cref="TryWithoutIO"/> skips compaction, the buffer start pointer is
    /// already restored before this method is called, so compaction shifts from the
    /// correct position.
    /// <para>
    /// When a pending token already spans the entire buffer (e.g. a very long inline
    /// string) there are zero consumed bytes to compact away, so the buffer is grown
    /// instead — otherwise this method would report success without reading anything,
    /// and the caller's parse/refill loop would spin forever re-parsing the same bytes.
    /// </para>
    /// </remarks>
    internal async ValueTask<bool> RefillAsync(CancellationToken ct = default)
    {
        _ioSkipped = false;

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
            if (space == 0)
            {
                Grow();
                space = _buf.Length - _end;
            }

            while (space > 0)
            {
                int bytesRead = await _source.ReadAsync(_buf.AsMemory(_end, space), ct).ConfigureAwait(false);
                if (bytesRead == 0)
                {
                    _streamDone = true;
                    break;
                }
                _end += bytesRead;
                space -= bytesRead;
            }
        }

        return _end > _start;
    }

    /// <summary>
    /// Doubles the buffer's capacity, preserving all unconsumed bytes at the front.
    /// Only called when a pending token doesn't fit in the current buffer at all
    /// (no bytes available to compact away).
    /// </summary>
    private void Grow()
    {
        byte[] grown = ArrayPool<byte>.Shared.Rent(_buf.Length * 2);
        _buf.AsSpan(0, _end).CopyTo(grown);
        ArrayPool<byte>.Shared.Return(_buf);
        _buf = grown;
    }

    /// <summary>True when the stream is exhausted and <see cref="Span"/> is empty.</summary>
    internal bool IsExhausted => _streamDone && _start >= _end;

    /// <summary>
    /// Attempts <paramref name="parse"/> without performing any I/O. Returns
    /// <see langword="true"/> when the parse succeeded entirely from buffered data.
    /// Returns <see langword="false"/> when a refill was needed — the buffer start
    /// pointer is rewound to its pre-parse position so the caller can call
    /// <see cref="RefillAsync"/> and retry.
    /// </summary>
    internal bool TryWithoutIO(Func<bool> parse)
    {
        int savedStart = _start;
        _ioSkipped = false;
        _noIO = true;
        bool result = parse();
        _noIO = false;

        if (result && !_ioSkipped)
        {
            return true;
        }

        if (_ioSkipped)
        {
            _start = savedStart;
        }

        return false;
    }

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
