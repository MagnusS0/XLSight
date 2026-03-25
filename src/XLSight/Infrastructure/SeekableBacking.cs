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

    public static SeekableBacking Create(Stream input)
    {
        ArgumentNullException.ThrowIfNull(input);

        if (input is MemoryStream memoryStream && memoryStream.TryGetBuffer(out _))
        {
            return new SeekableBacking(memoryStream, ownsStream: false);
        }

        if (!input.CanSeek)
        {
            ThrowHelpers.ThrowNonSeekableStreamRequiresAsync();
        }

        var copy = new MemoryStream();
        input.Position = 0;
        input.CopyTo(copy);
        copy.Position = 0;
        return new SeekableBacking(copy, ownsStream: true);
    }

    public static async ValueTask<SeekableBacking> CreateAsync(
        Stream input,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);

        if (input is MemoryStream memoryStream && memoryStream.TryGetBuffer(out _))
        {
            return new SeekableBacking(memoryStream, ownsStream: false);
        }

        var copy = new MemoryStream();
        if (input.CanSeek)
        {
            input.Position = 0;
        }

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
