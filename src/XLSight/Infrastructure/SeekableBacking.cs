namespace XLSight.Infrastructure;

internal sealed class SeekableBacking : IDisposable, IAsyncDisposable
{
    private bool _disposed;

    private SeekableBacking(Stream stream, bool ownsStream)
    {
        Stream = stream;
        OwnsStream = ownsStream;
    }

    public Stream Stream { get; }

    public bool OwnsStream { get; }

    public static SeekableBacking Create(Stream input, bool takeOwnership = false)
    {
        ArgumentNullException.ThrowIfNull(input);

        // Already a usable MemoryStream — use directly, caller owns it
        if (input is MemoryStream ms && ms.TryGetBuffer(out _))
        {
            return new SeekableBacking(ms, ownsStream: false);
        }

        // Any seekable stream can be used directly by ZipArchive — no copy needed
        if (input.CanSeek)
        {
            input.Position = 0;
            return new SeekableBacking(input, ownsStream: takeOwnership);
        }

        ThrowHelpers.ThrowNonSeekableStreamRequiresAsync();
        return null!; // unreachable
    }

    public static async ValueTask<SeekableBacking> CreateAsync(
        Stream input,
        bool takeOwnership = false,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);

        // Already a usable MemoryStream
        if (input is MemoryStream ms && ms.TryGetBuffer(out _))
        {
            return new SeekableBacking(ms, ownsStream: false);
        }

        // Seekable — use directly, no copy
        if (input.CanSeek)
        {
            input.Position = 0;
            return new SeekableBacking(input, ownsStream: takeOwnership);
        }

        // Non-seekable (e.g. HttpResponseStream) — must buffer
        var copy = new MemoryStream();
        await input.CopyToAsync(copy, cancellationToken).ConfigureAwait(false);
        copy.Position = 0;
        return new SeekableBacking(copy, ownsStream: true);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        if (OwnsStream)
        {
            Stream.Dispose();
        }

        _disposed = true;
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        if (OwnsStream)
        {
            await Stream.DisposeAsync().ConfigureAwait(false);
        }

        _disposed = true;
    }
}
