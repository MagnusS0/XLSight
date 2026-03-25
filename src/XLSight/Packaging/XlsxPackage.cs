using System.IO.Compression;
using XLSight.Infrastructure;

namespace XLSight.Packaging;

internal sealed class XlsxPackage : IDisposable, IAsyncDisposable
{
    private bool _disposed;

    private XlsxPackage(SeekableBacking backing, ZipArchive archive)
    {
        _backing = backing;
        _archive = archive;
    }

    private readonly SeekableBacking _backing;
    private readonly ZipArchive _archive;

    public IReadOnlyList<ZipArchiveEntry> Entries
    {
        get
        {
            ThrowIfDisposed();
            return _archive.Entries;
        }
    }

    public static XlsxPackage Open(Stream input)
    {
        SeekableBacking backing = SeekableBacking.Create(input);
        try
        {
            var archive = new ZipArchive(backing.Stream, ZipArchiveMode.Read, leaveOpen: true);
            return new XlsxPackage(backing, archive);
        }
        catch
        {
            backing.Dispose();
            throw;
        }
    }

    public static async ValueTask<XlsxPackage> OpenAsync(
        Stream input,
        CancellationToken cancellationToken = default)
    {
        SeekableBacking backing = await SeekableBacking.CreateAsync(input, cancellationToken).ConfigureAwait(false);
        try
        {
            ZipArchive archive = await ZipArchive.CreateAsync(
                backing.Stream,
                ZipArchiveMode.Read,
                leaveOpen: true,
                entryNameEncoding: null,
                cancellationToken).ConfigureAwait(false);

            return new XlsxPackage(backing, archive);
        }
        catch
        {
            await backing.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    public ZipArchiveEntry? GetEntry(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        ThrowIfDisposed();

        string normalizedPath = PathNormalizer.Normalize(path);
        return _archive.GetEntry(normalizedPath)
            ?? _archive.GetEntry(path)
            ?? _archive.Entries.FirstOrDefault(entry =>
                string.Equals(
                    PathNormalizer.Normalize(entry.FullName),
                    normalizedPath,
                    StringComparison.OrdinalIgnoreCase));
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _archive.Dispose();
        _backing.Dispose();
        _disposed = true;
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        await _archive.DisposeAsync().ConfigureAwait(false);
        await _backing.DisposeAsync().ConfigureAwait(false);
        _disposed = true;
    }

    private void ThrowIfDisposed()
    {
        if (_disposed)
        {
            ThrowHelpers.ThrowObjectDisposed(nameof(XlsxPackage));
        }
    }
}
