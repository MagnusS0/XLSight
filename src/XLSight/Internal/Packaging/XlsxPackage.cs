using System.IO.Compression;

namespace XLSight.Internal.Packaging;

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

    /// <summary>True when the workbook was opened from a file path and concurrent entry reads are safe.</summary>
    public bool IsFileBacked => _backing.Stream is FileStream;

    public IReadOnlyList<ZipArchiveEntry> Entries
    {
        get
        {
            ThrowIfDisposed();
            return _archive.Entries;
        }
    }

    public static XlsxPackage Open(Stream input, bool ownsStream = false)
    {
        SeekableBacking backing = SeekableBacking.Create(input, takeOwnership: ownsStream);
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
        bool ownsStream = false,
        CancellationToken cancellationToken = default)
    {
        SeekableBacking backing = await SeekableBacking.CreateAsync(input, takeOwnership: ownsStream, cancellationToken).ConfigureAwait(false);
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

        return FindEntry(_archive, path);
    }

    /// <summary>
    /// Opens a fresh, independent <see cref="ZipArchive"/> for a single entry.
    /// Safe to call concurrently when <see cref="IsFileBacked"/> is true.
    /// Returns null if no file path is available or the entry is not found.
    /// </summary>
    internal Stream? TryOpenFreshEntry(string entryPath)
    {
        if (_backing.Stream is not FileStream backingFs)
        {
            return null;
        }

        var fs = new FileStream(backingFs.Name, FileMode.Open, FileAccess.Read, FileShare.Read,
                                bufferSize: 4096, useAsync: false);
        var zip = new ZipArchive(fs, ZipArchiveMode.Read, leaveOpen: false);

        var entry = FindEntry(zip, entryPath);

        if (entry is null)
        {
            zip.Dispose();
            return null;
        }

        return new OwnedEntryStream(new BufferedStream(entry.Open(), 65536), zip);
    }

    private static ZipArchiveEntry? FindEntry(ZipArchive archive, string path)
    {
        string normalizedPath = PathNormalizer.Normalize(path);
        return archive.GetEntry(normalizedPath)
            ?? archive.GetEntry(path)
            ?? archive.Entries.FirstOrDefault(entry =>
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

/// <summary>
/// Wraps a zip entry stream and disposes an <see cref="IDisposable"/> owner (the ZipArchive)
/// when this stream is disposed, ensuring the archive stays alive until the entry is fully read.
/// </summary>
file sealed class OwnedEntryStream(Stream inner, IDisposable owner) : Stream
{
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
    public override long Seek(long offset, SeekOrigin origin) => inner.Seek(offset, origin);
    public override void SetLength(long value) => inner.SetLength(value);
    public override void Write(byte[] buffer, int offset, int count) => inner.Write(buffer, offset, count);
    public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken ct)
        => inner.ReadAsync(buffer, offset, count, ct);
    public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken ct = default)
        => inner.ReadAsync(buffer, ct);

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            inner.Dispose();
            owner.Dispose();
        }
        base.Dispose(disposing);
    }

    public override async ValueTask DisposeAsync()
    {
        await inner.DisposeAsync().ConfigureAwait(false);
        if (owner is IAsyncDisposable asyncOwner)
        {
            await asyncOwner.DisposeAsync().ConfigureAwait(false);
        }
        else
        {
            owner.Dispose();
        }
        await base.DisposeAsync().ConfigureAwait(false);
    }
}
