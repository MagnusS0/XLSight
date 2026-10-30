namespace XLSight.Internal.Packaging;

/// <summary>
/// Wraps an entry stream and disposes its owning archive with it.
/// </summary>
/// <param name="inner">The entry stream.</param>
/// <param name="owner">Released when this stream is disposed.</param>
/// <param name="completeAsyncReadsSynchronously">
/// True when <paramref name="inner"/> ultimately reads a <see cref="FileStream"/> opened for
/// synchronous I/O. Such a stream's <c>ReadAsync</c> runs the same blocking read on a thread-pool
/// thread (one queued work item per 8 KB of compressed input, through <c>DeflateStream</c>), and
/// cancellation is only observed before the read starts. Reading inline, as
/// <see cref="MemoryStream"/> does, does the same work without the hops and keeps that
/// cancellation behaviour. Reads stay asynchronous while a <see cref="SynchronizationContext"/>
/// or a non-default <see cref="TaskScheduler"/> is current, so a UI or single-threaded caller
/// is still moved off its thread by the first read; the scan continues on the pool
/// (the readers use <c>ConfigureAwait(false)</c>) and reads inline from there.
/// </param>
internal sealed class OwnedEntryStream(Stream inner, IDisposable owner, bool completeAsyncReadsSynchronously = false) : Stream
{
    private bool _disposed;

    public override bool CanRead => inner.CanRead;
    public override bool CanSeek => inner.CanSeek;
    public override bool CanWrite => inner.CanWrite;
    public override long Length => inner.Length;

    public override long Position
    {
        get => inner.Position;
        set => inner.Position = value;
    }

    public override void Flush() => inner.Flush();
    public override int Read(byte[] buffer, int offset, int count) => inner.Read(buffer, offset, count);
    public override int Read(Span<byte> buffer) => inner.Read(buffer);
    public override int ReadByte() => inner.ReadByte();
    public override long Seek(long offset, SeekOrigin origin) => inner.Seek(offset, origin);
    public override void SetLength(long value) => inner.SetLength(value);
    public override void Write(byte[] buffer, int offset, int count) => inner.Write(buffer, offset, count);
    public override void WriteByte(byte value) => inner.WriteByte(value);
    public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken ct) =>
        completeAsyncReadsSynchronously
            ? ReadAsync(buffer.AsMemory(offset, count), ct).AsTask()
            : inner.ReadAsync(buffer, offset, count, ct);

    public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken ct = default)
    {
        if (!completeAsyncReadsSynchronously
            || SynchronizationContext.Current is not null
            || TaskScheduler.Current != TaskScheduler.Default)
        {
            return inner.ReadAsync(buffer, ct);
        }

        if (ct.IsCancellationRequested)
        {
            return ValueTask.FromCanceled<int>(ct);
        }

        try
        {
            return new ValueTask<int>(inner.Read(buffer.Span));
        }
        catch (Exception ex)
        {
            return ValueTask.FromException<int>(ex);
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing && !_disposed)
        {
            _disposed = true;
            try
            {
                inner.Dispose();
            }
            finally
            {
                owner.Dispose();
            }
        }

        base.Dispose(disposing);
    }

    public override async ValueTask DisposeAsync()
    {
        try
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            try
            {
                await inner.DisposeAsync().ConfigureAwait(false);
            }
            finally
            {
                if (owner is IAsyncDisposable asyncOwner)
                {
                    await asyncOwner.DisposeAsync().ConfigureAwait(false);
                }
                else
                {
                    owner.Dispose();
                }
            }
        }
        finally
        {
            await base.DisposeAsync().ConfigureAwait(false);
        }
    }
}
