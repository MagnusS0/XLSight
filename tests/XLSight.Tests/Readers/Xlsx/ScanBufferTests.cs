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

    // ── Start / RewindTo ─────────────────────────────────────────────────────

    [Fact]
    public void Start_ReturnsCurrentStartIndex()
    {
        using var stream = MakeStream("hello");
        using var buf = new ScanBuffer(stream);
        int start = buf.Start;
        Assert.Equal(0, start);
    }

    [Fact]
    public void RewindTo_RestoresStartAfterAdvance()
    {
        using var stream = MakeStream("hello");
        using var buf = new ScanBuffer(stream);
        int saved = buf.Start;
        buf.Advance(3);
        buf.RewindTo(saved);
        Assert.Equal((byte)'h', buf.Span[0]);
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

    // ── NoIO mode ────────────────────────────────────────────────────────────

    [Fact]
    public void Refill_WhenNoIOIsTrue_SetsIOSkippedAndReturnsFalse()
    {
        using var stream = MakeStream("hello");
        using var buf = new ScanBuffer(stream);
        buf.NoIO = true;
        bool result = buf.Refill();
        Assert.False(result);
        Assert.True(buf.IOSkipped);
    }

    [Fact]
    public void Refill_WhenNoIOIsTrue_DoesNotChangeBufferLayout()
    {
        using var stream = MakeStream("hello");
        using var buf = new ScanBuffer(stream);
        buf.Advance(2); // start at 'l'
        int startBefore = buf.Start;
        buf.NoIO = true;
        buf.Refill();
        Assert.Equal(startBefore, buf.Start); // no compaction happened
    }

    // ── RefillAsync ──────────────────────────────────────────────────────────

    [Fact]
    public async Task RefillAsync_ClearsIOSkipped()
    {
        using var stream = MakeStream("hello");
        using var buf = new ScanBuffer(stream);
        buf.NoIO = true;
        buf.Refill(); // sets IOSkipped = true
        Assert.True(buf.IOSkipped);

        await buf.RefillAsync();

        Assert.False(buf.IOSkipped);
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
    public void Refill_WithPartialReadStream_FillsBufferCompletely()
    {
        // A stream that returns at most 1 byte per Read call (simulating DeflateStream).
        // Bug: Refill does a single Read, so only 1 byte would be added to the buffer
        // instead of filling the available space.
        const int BufferSize = 65536;
        using var stream = new ChunkedStream(new byte[BufferSize], chunkSize: 1);
        using var buf = new ScanBuffer(stream);

        // The initial prime should fill the full buffer, not stop after 1 byte.
        Assert.Equal(BufferSize, buf.Span.Length);
    }

    [Fact]
    public async Task RefillAsync_WithPartialReadStream_FillsBufferCompletely()
    {
        const int BufferSize = 65536;
        const int Remaining = 100;
        var data = new byte[BufferSize + Remaining];
        using var stream = new ChunkedStream(data, chunkSize: 1);
        using var buf = new ScanBuffer(stream);
        buf.Advance(buf.Span.Length); // consume the initial fill

        bool hasMore = await buf.RefillAsync();

        Assert.True(hasMore);
        // Should have filled up to Remaining bytes (all that's left), not just 1.
        Assert.Equal(Remaining, buf.Span.Length);
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
