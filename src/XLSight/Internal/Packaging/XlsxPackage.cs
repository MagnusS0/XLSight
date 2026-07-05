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

    // Pooled (FileStream, ZipArchive) pairs from TryOpenFreshEntryCore: constructing a ZipArchive
    // parses the whole central directory, so reusing one instead of reopening avoids that cost on
    // every fresh-entry open (e.g. once per sheet during parallel analysis). _poolLock also guards
    // _disposed so a return-to-pool can never race past a drain: either it observes _disposed and
    // disposes the archive itself, or it lands in the pool before Dispose's drain sees it.
    private readonly Stack<ZipArchive> _archivePool = new();
    private readonly Lock _poolLock = new();

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

        ZipArchive zip = RentArchive(backingFs.Name);

        var entry = FindEntry(zip, entryPath);

        if (entry is null)
        {
            ReturnArchive(zip);
            return null;
        }

        Stream raw = entry.Open();
        // addBuffer=false when the caller supplies its own pool buffer (e.g. XlsbRecordIterator),
        // avoiding a redundant 65 KB heap allocation that would otherwise pressure the GC.
        return new OwnedEntryStream(addBuffer ? new BufferedStream(raw, 65536) : raw, new PooledArchiveHandle(this, zip));
    }

    private ZipArchive RentArchive(string path)
    {
        lock (_poolLock)
        {
            if (_archivePool.TryPop(out var pooled))
            {
                return pooled;
            }
        }

        var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read,
                                bufferSize: 4096, useAsync: false);
        return new ZipArchive(fs, ZipArchiveMode.Read, leaveOpen: false);
    }

    private void ReturnArchive(ZipArchive archive)
    {
        lock (_poolLock)
        {
            if (!_disposed)
            {
                _archivePool.Push(archive);
                return;
            }
        }

        // The package was disposed while this archive was on loan: pooling it would leak the
        // FileStream past Dispose's drain, which already ran.
        archive.Dispose();
    }

    // OwnedEntryStream disposes its inner entry stream before this owner, so the archive is only
    // ever returned once fully idle — safe for the next borrower to open a new entry on it.
    private sealed class PooledArchiveHandle(XlsxPackage package, ZipArchive archive) : IDisposable, IAsyncDisposable
    {
        public void Dispose() => package.ReturnArchive(archive);

        public ValueTask DisposeAsync()
        {
            package.ReturnArchive(archive);
            return ValueTask.CompletedTask;
        }
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

        ZipArchive[] pooled;
        lock (_poolLock)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            pooled = [.. _archivePool];
            _archivePool.Clear();
        }

        foreach (var archive in pooled)
        {
            archive.Dispose();
        }

        _archive.Dispose();
        _backing.Dispose();
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        ZipArchive[] pooled;
        lock (_poolLock)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            pooled = [.. _archivePool];
            _archivePool.Clear();
        }

        foreach (var archive in pooled)
        {
            await archive.DisposeAsync().ConfigureAwait(false);
        }

        await _archive.DisposeAsync().ConfigureAwait(false);
        await _backing.DisposeAsync().ConfigureAwait(false);
    }

    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(_disposed, this);
}
