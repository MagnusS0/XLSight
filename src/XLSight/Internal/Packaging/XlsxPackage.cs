using System.IO.Compression;

namespace XLSight.Internal.Packaging;

internal sealed class XlsxPackage : IDisposable, IAsyncDisposable
{
    private const int MaxPooledArchives = 4;
    private bool _disposed;

    private XlsxPackage(SeekableBacking backing, ZipArchive archive)
    {
        _backing = backing;
        _archive = archive;
    }

    private readonly SeekableBacking _backing;
    private readonly ZipArchive _archive;

    // Fresh archives avoid sharing entry streams across concurrent scans. Reusing an idle
    // archive avoids reparsing the ZIP central directory for every fresh entry open.
    private readonly Stack<ZipArchive> _archivePool = new();
    private readonly Lock _poolLock = new();

    internal int PooledArchiveCount
    {
        get
        {
            lock (_poolLock)
            {
                return _archivePool.Count;
            }
        }
    }

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

        Stream? raw = null;
        try
        {
            raw = entry.Open();
            // addBuffer=false when the caller supplies its own pool buffer (e.g. XlsbRecordIterator),
            // avoiding a redundant 65 KB heap allocation that would otherwise pressure the GC.
            Stream inner = addBuffer ? new BufferedStream(raw, 65536) : raw;
            var owned = new OwnedEntryStream(inner, new PooledArchiveHandle(this, zip));
            raw = null;
            return owned;
        }
        catch
        {
            try
            {
                raw?.Dispose();
            }
            finally
            {
                ReturnArchive(zip);
            }

            throw;
        }
    }

    private ZipArchive RentArchive(string path)
    {
        lock (_poolLock)
        {
            if (_archivePool.TryPop(out ZipArchive? pooled))
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
            if (!_disposed && _archivePool.Count < MaxPooledArchives)
            {
                _archivePool.Push(archive);
                return;
            }
        }

        // The package was disposed while this archive was on loan, or the idle pool is full.
        archive.Dispose();
    }

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

        foreach (ZipArchive archive in pooled)
        {
            archive.Dispose();
        }

        _archive.Dispose();
        _backing.Dispose();
    }

    public async ValueTask DisposeAsync()
    {
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

        foreach (ZipArchive archive in pooled)
        {
            await archive.DisposeAsync().ConfigureAwait(false);
        }

        await _archive.DisposeAsync().ConfigureAwait(false);
        await _backing.DisposeAsync().ConfigureAwait(false);
    }

    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(_disposed, this);
}
