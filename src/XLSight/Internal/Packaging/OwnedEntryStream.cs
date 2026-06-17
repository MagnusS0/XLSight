namespace XLSight.Internal.Packaging;

/// <summary>
/// Wraps an entry stream and disposes its owning archive with it.
/// </summary>
internal sealed class OwnedEntryStream(Stream inner, IDisposable owner) : Stream
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
        inner.ReadAsync(buffer, offset, count, ct);
    public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken ct = default) =>
        inner.ReadAsync(buffer, ct);

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
