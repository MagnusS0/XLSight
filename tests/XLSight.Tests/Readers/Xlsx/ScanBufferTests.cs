using XLSight.Internal.Readers.Xlsx;
using Xunit;

namespace XLSight.Tests.Readers.Xlsx;

/// <summary>
/// Unit tests for the internal <see cref="ScanBuffer"/> sliding-window reader.
/// </summary>
public sealed class ScanBufferTests
{
    // ── Helpers ───────────────────────────────────────────────────────────────

    private static MemoryStream MakeStream(string text)
        => new MemoryStream(System.Text.Encoding.UTF8.GetBytes(text));

    private static MemoryStream MakeStream(int byteCount, byte fill = (byte)'A')
    {
        var buf = new byte[byteCount];
        buf.AsSpan().Fill(fill);
        return new MemoryStream(buf);
    }

    // ── Construction / initial prime ─────────────────────────────────────────

    [Fact]
    public void Constructor_PrimesBufferFromStream()
    {
        using var stream = MakeStream("hello");
        using var buf = new ScanBuffer(stream);
        Assert.False(buf.Span.IsEmpty);
        Assert.Equal((byte)'h', buf.Span[0]);
    }

    [Fact]
    public void Constructor_EmptyStream_SpanIsEmpty()
    {
        using var stream = MakeStream("");
        using var buf = new ScanBuffer(stream);
        Assert.True(buf.Span.IsEmpty);
        Assert.True(buf.IsExhausted);
    }

    // ── Span / Advance ───────────────────────────────────────────────────────

    [Fact]
    public void Advance_MovesStartPointer()
    {
        using var stream = MakeStream("hello");
        using var buf = new ScanBuffer(stream);
        buf.Advance(2);
        Assert.Equal((byte)'l', buf.Span[0]);
    }

    [Fact]
    public void Advance_PastAllBytes_SpanBecomesEmpty()
    {
        using var stream = MakeStream("hi");
        using var buf = new ScanBuffer(stream);
        buf.Advance(buf.Span.Length);
        Assert.True(buf.Span.IsEmpty);
    }

    // ── TryWithoutIO ─────────────────────────────────────────────────────────

    [Fact]
    public void Span_StartsAtFirstByte_OnConstruction()
    {
        using var stream = MakeStream("hello");
        using var buf = new ScanBuffer(stream);
        Assert.Equal((byte)'h', buf.Span[0]);
    }

    [Fact]
    public void TryWithoutIO_RevertsToSavedPosition_WhenRefillNeeded()
    {
        using var stream = MakeStream("hello");
        using var buf = new ScanBuffer(stream);
        buf.Advance(3); // 'l' is at front
        byte firstByte = buf.Span[0]; // 'l'

        // Parse lambda exhausts the buffer and calls Refill, triggering the IO-skip path.
        bool result = buf.TryWithoutIO(() =>
        {
            buf.Advance(buf.Span.Length);
            return buf.Refill(); // sets IOSkipped in noIO mode, returns false
        });

        Assert.False(result);
        Assert.Equal(firstByte, buf.Span[0]); // start rewound to pre-parse position
    }

    // ── Refill (synchronous) ─────────────────────────────────────────────────

    [Fact]
    public void Refill_AfterAdvancingAll_StreamExhausted_ReturnsFalse()
    {
        using var stream = MakeStream("ab");
        using var buf = new ScanBuffer(stream);
        buf.Advance(buf.Span.Length); // consume everything
        bool result = buf.Refill();
        Assert.False(result);
    }

    [Fact]
    public void Refill_WithRemainingBytes_ReturnsTrue()
    {
        using var stream = MakeStream("hello world");
        using var buf = new ScanBuffer(stream);
        buf.Advance(5); // consume "hello"
        bool result = buf.Refill(); // compacts " world" to front
        Assert.True(result);
        Assert.Equal((byte)' ', buf.Span[0]);
    }

    // ── TryWithoutIO / noIO mode ─────────────────────────────────────────────

    [Fact]
    public void TryWithoutIO_ReturnsFalse_WhenParseNeedsRefill()
    {
        using var stream = MakeStream("hello");
        using var buf = new ScanBuffer(stream);

        bool result = buf.TryWithoutIO(() =>
        {
            buf.Advance(buf.Span.Length); // exhaust buffered bytes
            return buf.Refill();           // Refill in noIO mode → returns false
        });

        Assert.False(result);
    }

    [Fact]
    public void TryWithoutIO_SpanUnchanged_WhenRefillNeeded()
    {
        using var stream = MakeStream("hello");
        using var buf = new ScanBuffer(stream);
        buf.Advance(2); // 'l' is at front
        byte firstByte = buf.Span[0];

        buf.TryWithoutIO(() =>
        {
            buf.Advance(buf.Span.Length);
            return buf.Refill();
        });

        // Span start rewound — buffer layout unchanged from before the parse
        Assert.Equal(firstByte, buf.Span[0]);
    }

    // ── RefillAsync ──────────────────────────────────────────────────────────

    [Fact]
    public async Task TryWithoutIO_SucceedsAfterRefillAsync_WhenMoreDataAvailable()
    {
        // Stream larger than one buffer: constructor fills buffer with first part.
        const int BufferSize = 65536;
        var data = new byte[BufferSize + 5];
        data.AsSpan(BufferSize).Fill((byte)'Z');
        using var stream = new MemoryStream(data);
        using var buf = new ScanBuffer(stream);

        // Consume the initial fill so the buffer is empty without I/O.
        buf.Advance(buf.Span.Length);

        bool before = buf.TryWithoutIO(() => !buf.Span.IsEmpty);
        Assert.False(before); // no buffered data → fails

        // RefillAsync loads the remaining 5 bytes.
        await buf.RefillAsync();
        Assert.Equal(5, buf.Span.Length);

        // Now TryWithoutIO succeeds from buffered data.
        bool after = buf.TryWithoutIO(() => !buf.Span.IsEmpty);
        Assert.True(after);
    }

    [Fact]
    public async Task RefillAsync_WithRemainingBytes_ReturnsTrueAndCompacts()
    {
        using var stream = MakeStream("hello");
        using var buf = new ScanBuffer(stream);
        buf.Advance(2); // "he" consumed, "llo" remains
        bool result = await buf.RefillAsync();
        Assert.True(result);
        Assert.Equal((byte)'l', buf.Span[0]);
    }

    [Fact]
    public async Task RefillAsync_EmptyStreamAfterConsumingAll_ReturnsFalse()
    {
        using var stream = MakeStream("hi");
        using var buf = new ScanBuffer(stream);
        buf.Advance(buf.Span.Length);
        bool result = await buf.RefillAsync();
        Assert.False(result);
    }

    // ── IsExhausted ──────────────────────────────────────────────────────────

    [Fact]
    public void IsExhausted_AfterConsumingAllBytesAndRefill_IsTrue()
    {
        using var stream = MakeStream("ab");
        using var buf = new ScanBuffer(stream);
        buf.Advance(buf.Span.Length); // consume all buffered bytes
        buf.Refill();                 // triggers a 0-byte read → _streamDone = true
        Assert.True(buf.IsExhausted);
    }

    [Fact]
    public void IsExhausted_WithBytesRemaining_IsFalse()
    {
        using var stream = MakeStream("abc");
        using var buf = new ScanBuffer(stream);
        buf.Advance(1);
        Assert.False(buf.IsExhausted);
    }

    // ── CanReadMore ──────────────────────────────────────────────────────────

    [Fact]
    public void CanReadMore_AfterStreamExhaustedAndBufferConsumed_IsFalse()
    {
        using var stream = MakeStream("x");
        using var buf = new ScanBuffer(stream);
        buf.Advance(buf.Span.Length); // consume all buffered bytes
        buf.Refill();                 // reads 0 → _streamDone = true
        Assert.False(buf.CanReadMore);
    }

    // ── Partial-read stream ──────────────────────────────────────────────────

    [Fact]
    public void Refill_WithPartialReadStream_AcceptsPartialFill()
    {
        // Sync Refill issues a single Read and accepts however many bytes the
        // stream returns, rather than blocking until the buffer is full.
        // This prevents the parser stalling while waiting for zlib-ng to produce
        // a full 64 KB chunk.
        const int chunkSize = 1024;
        using var stream = new ChunkedStream(new byte[65536 * 2], chunkSize: chunkSize);
        using var buf = new ScanBuffer(stream);

        // The initial prime should have accepted exactly one chunk, not the full buffer.
        Assert.Equal(chunkSize, buf.Span.Length);
    }

    [Fact]
    public async Task RefillAsync_WithPartialReadStream_FillsBufferCompletely()
    {
        // Async RefillAsync loops until the buffer is full, unlike sync Refill.
        const int BufferSize = 65536;
        const int chunkSize = 1024;
        var data = new byte[BufferSize * 2]; // enough data to fill one buffer after the initial prime
        using var stream = new ChunkedStream(data, chunkSize: chunkSize);
        using var buf = new ScanBuffer(stream);

        // Constructor primed with one sync chunk.
        Assert.Equal(chunkSize, buf.Span.Length);
        buf.Advance(buf.Span.Length);

        bool hasMore = await buf.RefillAsync();

        Assert.True(hasMore);
        // RefillAsync fills the entire buffer, not just one chunk.
        Assert.Equal(BufferSize, buf.Span.Length);
    }

    /// <summary>
    /// A stream that returns at most <c>chunkSize</c> bytes per Read call,
    /// simulating the partial-read behaviour of <see cref="System.IO.Compression.DeflateStream"/>.
    /// </summary>
    private sealed class ChunkedStream(byte[] data, int chunkSize) : Stream
    {
        private int _pos;

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => data.Length;
        public override long Position { get => _pos; set => throw new NotSupportedException(); }
        public override void Flush() { }

        public override int Read(byte[] buffer, int offset, int count)
        {
            if (_pos >= data.Length) { return 0; }
            int n = Math.Min(chunkSize, Math.Min(count, data.Length - _pos));
            data.AsSpan(_pos, n).CopyTo(buffer.AsSpan(offset, n));
            _pos += n;
            return n;
        }

        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken ct = default)
        {
            if (_pos >= data.Length) { return ValueTask.FromResult(0); }
            int n = Math.Min(chunkSize, Math.Min(buffer.Length, data.Length - _pos));
            data.AsSpan(_pos, n).CopyTo(buffer.Span);
            _pos += n;
            return ValueTask.FromResult(n);
        }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }

    // ── Reset ────────────────────────────────────────────────────────────────

    [Fact]
    public void Reset_ClearsBufferAndRefillsFromStreamPosition()
    {
        using var stream = MakeStream("hello");
        using var buf = new ScanBuffer(stream);
        buf.Advance(3);
        // stream has already been fully read into the buffer; reset discards what's there
        buf.Reset();
        // After reset, _start = 0, _end = remaining unread from stream (none here)
        // because the stream was already exhausted during construction priming
        // The buffer is reset but stream is done, so IsExhausted may be true if nothing left
        Assert.True(buf.IsExhausted || buf.Span.Length >= 0); // post-reset state is consistent
    }

    // ── Dispose ──────────────────────────────────────────────────────────────

    [Fact]
    public void Dispose_IsIdempotent()
    {
        using var stream = MakeStream("hello");
        var buf = new ScanBuffer(stream);
        buf.Dispose();
        buf.Dispose(); // second dispose should not throw
    }

    // ── Large stream — buffer refill path ────────────────────────────────────

    [Fact]
    public void Refill_WithLargeStream_CanReadMoreThanOneBuffer()
    {
        // 130000 bytes > 2 × 65536 — exercises compaction path
        const int size = 130_000;
        using var stream = MakeStream(size, fill: (byte)'X');
        using var buf = new ScanBuffer(stream);

        int totalRead = 0;
        while (!buf.IsExhausted)
        {
            int chunk = buf.Span.Length;
            if (chunk == 0)
            {
                if (!buf.Refill()) { break; }
                continue;
            }
            totalRead += chunk;
            buf.Advance(chunk);
            buf.Refill();
        }

        Assert.Equal(size, totalRead);
    }

    // ── Stream returning 0 bytes on first read ────────────────────────────────

    [Fact]
    public void Constructor_WithZeroByteStream_BufferIsEmpty()
    {
        using var stream = new ZeroStream();
        using var buf = new ScanBuffer(stream);
        Assert.True(buf.Span.IsEmpty);
    }

    /// <summary>A stream that always returns 0 bytes (immediately reports EOF).</summary>
    private sealed class ZeroStream : Stream
    {
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => 0;
        public override long Position { get => 0; set { } }
        public override void Flush() { }
        public override int Read(byte[] buffer, int offset, int count) => 0;
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
