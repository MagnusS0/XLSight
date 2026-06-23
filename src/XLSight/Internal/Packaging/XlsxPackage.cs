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
    /// Opens an entry stream, preferring a fresh independent archive for concurrent access.
    /// Returns null when the entry does not exist.
    /// </summary>
    internal Stream? TryOpenEntryBuffered(string path)
    {
        ThrowIfDisposed();

        Stream? fresh = TryOpenFreshEntry(path);
        if (fresh is not null)
        {
            return fresh;
        }

        return GetEntry(path)?.OpenBuffered();
    }

    /// <summary>Builds the OPC relationships path for a given part path.</summary>
    internal static string BuildRelationshipsPath(string ownerPath)
    {
        int slash = ownerPath.LastIndexOf('/');
        string directory = slash >= 0 ? ownerPath[..slash] : string.Empty;
        string fileName = slash >= 0 ? ownerPath[(slash + 1)..] : ownerPath;
        return string.IsNullOrEmpty(directory)
            ? $"_rels/{fileName}.rels"
            : $"{directory}/_rels/{fileName}.rels";
    }

    /// <summary>
    /// Opens a fresh, independent <see cref="ZipArchive"/> for a single entry.
    /// Safe to call concurrently when <see cref="IsFileBacked"/> is true.
    /// Returns null if no file path is available or the entry is not found.
    /// </summary>
    internal Stream? TryOpenFreshEntry(string entryPath) =>
        TryOpenFreshEntryCore(entryPath, addBuffer: true);

    internal Stream? TryOpenFreshEntryUnbuffered(string entryPath) =>
        TryOpenFreshEntryCore(entryPath, addBuffer: false);

    private OwnedEntryStream? TryOpenFreshEntryCore(string entryPath, bool addBuffer)
    {
        ThrowIfDisposed();

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

        Stream raw = entry.Open();
        // addBuffer=false when the caller supplies its own pool buffer (e.g. XlsbRecordIterator),
        // avoiding a redundant 65 KB heap allocation that would otherwise pressure the GC.
        return new OwnedEntryStream(addBuffer ? new BufferedStream(raw, 65536) : raw, zip);
    }

    private static ZipArchiveEntry? FindEntry(ZipArchive archive, string path)
    {
        string normalizedPath = path.Replace('\\', '/');
        return archive.GetEntry(normalizedPath)
            ?? archive.GetEntry(path)
            ?? archive.Entries.FirstOrDefault(entry =>
                string.Equals(
                    entry.FullName.Replace('\\', '/'),
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

    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(_disposed, this);
}
