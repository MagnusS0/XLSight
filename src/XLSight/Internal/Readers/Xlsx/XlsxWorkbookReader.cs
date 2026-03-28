using XLSight.Exceptions;
using XLSight.Internal.Metadata;
using XLSight.Internal.Packaging;
using XLSight.Internal.Sinks;
using XLSight.Models;
using XLSight.Models.Analysis;

namespace XLSight.Internal.Readers.Xlsx;

internal sealed class XlsxWorkbookReader : IWorkbookReader
{
    private readonly XlsxPackage _package;
    private readonly WorkbookMetadata _metadata;
    private readonly Lazy<SharedStringTable> _sharedStrings;
    private readonly Lazy<StyleTable> _styles;
    private volatile bool _disposed;

    internal XlsxWorkbookReader(XlsxPackage package, WorkbookMetadata metadata)
    {
        _package = package;
        _metadata = metadata;
        _sharedStrings = new Lazy<SharedStringTable>(LoadSharedStrings, LazyThreadSafetyMode.ExecutionAndPublication);
        _styles = new Lazy<StyleTable>(LoadStyles, LazyThreadSafetyMode.ExecutionAndPublication);
    }

    private SharedStringTable LoadSharedStrings()
    {
        var entry = _package.GetEntry("xl/sharedStrings.xml");
        if (entry is null)
        {
            return SharedStringTable.Empty;
        }

        using var stream = entry.OpenBuffered();
        return SharedStringsParser.Parse(stream);
    }

    private StyleTable LoadStyles()
    {
        var entry = _package.GetEntry("xl/styles.xml");
        if (entry is null)
        {
            return StyleTable.Default;
        }

        using var stream = entry.OpenBuffered();
        return StylesParser.Parse(stream);
    }

    public bool IsFileBacked => _package.IsFileBacked;

    public IReadOnlyList<string> SheetNames
    {
        get
        {
            ThrowIfDisposed();
            return _metadata.Sheets.Select(s => s.Name).ToArray();
        }
    }

    public bool IsDate1904
    {
        get
        {
            ThrowIfDisposed();
            return _metadata.UsesDate1904;
        }
    }

    public bool HasMacros
    {
        get
        {
            ThrowIfDisposed();
            return _metadata.HasMacros;
        }
    }

    public RangeResult ReadRange(string sheetName, ExcelRange range, ReadMode mode)
    {
        ThrowIfDisposed();

        var sheet = FindSheet(sheetName);

        if (range.IsUnbounded)
        {
            throw new RangeTooLargeException(0, ExcelLimits.MaxCells);
        }

        long cellCount = (long)range.Width * range.Height;
        if (cellCount > ExcelLimits.MaxCells)
        {
            throw new RangeTooLargeException(cellCount, ExcelLimits.MaxCells);
        }

        using var sheetStream = OpenSheetStream(sheet.Path);
        var buffer = new ExcelCellValue[cellCount];
        var sink = new RangeSink(range, buffer);
        XlsxSheetScanner.ScanSheet(sheetStream, _sharedStrings.Value, _styles.Value, _metadata.UsesDate1904, mode, range, ref sink);

        return new RangeResult
        {
            Sheet = sheetName,
            StartRow = range.TopLeft.Row,
            StartColumn = range.TopLeft.Column,
            Width = range.Width,
            Height = range.Height,
            Cells = buffer,
        };
    }

    public CellResult ReadCell(string sheetName, ExcelAddress address, ReadMode mode)
    {
        ThrowIfDisposed();

        var range = new ExcelRange(address, address);
        var result = ReadRange(sheetName, range, mode);

        return new CellResult
        {
            Sheet = sheetName,
            Row = address.Row,
            Column = address.Column,
            Value = result.Cells[0],
        };
    }

    public async Task<CellResult> ReadCellAsync(string sheetName, ExcelAddress address, ReadMode mode, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var range = new ExcelRange(address, address);
        var result = await ReadRangeAsync(sheetName, range, mode, ct).ConfigureAwait(false);
        return new CellResult
        {
            Sheet = sheetName,
            Row = address.Row,
            Column = address.Column,
            Value = result.Cells[0],
        };
    }

    public async Task<RangeResult> ReadRangeAsync(string sheetName, ExcelRange range, ReadMode mode, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        ThrowIfDisposed();

        var sheet = FindSheet(sheetName);

        if (range.IsUnbounded)
        {
            throw new RangeTooLargeException(0, ExcelLimits.MaxCells);
        }

        long cellCount = (long)range.Width * range.Height;
        if (cellCount > ExcelLimits.MaxCells)
        {
            throw new RangeTooLargeException(cellCount, ExcelLimits.MaxCells);
        }

        using var sheetStream = OpenSheetStream(sheet.Path);
        var buffer = new ExcelCellValue[cellCount];
        var sink = new RangeSink(range, buffer);
        XlsxSheetScanner.ScanSheet(sheetStream, _sharedStrings.Value, _styles.Value, _metadata.UsesDate1904, mode, range, ref sink);

        return new RangeResult
        {
            Sheet = sheetName,
            StartRow = range.TopLeft.Row,
            StartColumn = range.TopLeft.Column,
            Width = range.Width,
            Height = range.Height,
            Cells = buffer,
        };
    }

    public WorkbookInfo Analyze()
    {
        ThrowIfDisposed();

        var sheets = _metadata.Sheets
            .Select((s, i) => AnalyzeSheetCore(s, i))
            .ToList();

        var namedRanges = _metadata.NamedRanges
            .Select(nr => new NamedRange
            {
                Name = nr.Name,
                Sheet = nr.ScopeSheetName,
                Reference = nr.Reference,
            })
            .ToList();

        return new WorkbookInfo
        {
            Sheets = sheets,
            NamedRanges = namedRanges,
            HasMacros = _metadata.HasMacros,
            IsDate1904 = _metadata.UsesDate1904,
            AnalyzedAtUtc = DateTimeOffset.UtcNow,
        };
    }

    public SheetInfo AnalyzeSheet(string sheetName)
    {
        ThrowIfDisposed();

        var sheet = FindSheet(sheetName);
        int sheetIndex = _metadata.Sheets
            .Select((s, i) => (s, i))
            .First(t => string.Equals(t.s.Name, sheetName, StringComparison.OrdinalIgnoreCase))
            .i;

        return AnalyzeSheetCore(sheet, sheetIndex);
    }

    public async Task<WorkbookInfo> AnalyzeAsync(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        ThrowIfDisposed();

        var sheets = new List<SheetInfo>();
        foreach (var sheet in _metadata.Sheets)
        {
            ct.ThrowIfCancellationRequested();
            var sheetInfo = await AnalyzeSheetAsync(sheet.Name, ct).ConfigureAwait(false);
            sheets.Add(sheetInfo);
        }

        var namedRanges = _metadata.NamedRanges
            .Select(nr => new NamedRange
            {
                Name = nr.Name,
                Sheet = nr.ScopeSheetName,
                Reference = nr.Reference,
            })
            .ToArray();

        return new WorkbookInfo
        {
            Sheets = sheets,
            NamedRanges = namedRanges,
            HasMacros = _metadata.HasMacros,
            IsDate1904 = _metadata.UsesDate1904,
            AnalyzedAtUtc = DateTimeOffset.UtcNow,
        };
    }

    public async Task<SheetInfo> AnalyzeSheetAsync(string sheetName, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        ThrowIfDisposed();

        var sheet = FindSheet(sheetName);
        int sheetIndex = _metadata.Sheets
            .Select((s, i) => (s, i))
            .First(t => string.Equals(t.s.Name, sheetName, StringComparison.OrdinalIgnoreCase))
            .i;

        using var sheetStream = OpenSheetStream(sheet.Path);
        var sink = new AnalysisSink(_sharedStrings.Value);
        XlsxSheetScanner.ScanSheet(sheetStream, _sharedStrings.Value, _styles.Value, _metadata.UsesDate1904, ReadMode.Values, ExcelRange.Unbounded, ref sink);
        return sink.Build(sheetName, sheetIndex, []);
    }

    private SheetInfo AnalyzeSheetCore(WorkbookMetadata.WorkbookSheetInfo sheet, int sheetIndex)
    {
        using var sheetStream = OpenSheetStream(sheet.Path);

        var sink = new AnalysisSink(_sharedStrings.Value);
        XlsxSheetScanner.ScanSheet(sheetStream, _sharedStrings.Value, _styles.Value, _metadata.UsesDate1904, ReadMode.Values, ExcelRange.Unbounded, ref sink);

        return sink.Build(sheet.Name, sheetIndex, []);
    }

    public IEnumerable<ExcelRow> StreamRange(string sheetName, ExcelRange range, ReadMode mode)
    {
        ThrowIfDisposed();

        var sheet = FindSheet(sheetName);

        // Validation runs eagerly here; iteration is deferred to the private iterator below,
        // which owns the stream lifetime via 'using' inside the iterator body.
        return StreamRangeCore(OpenSheetStream(sheet.Path), range, mode);
    }

    // Private iterator — stream lifetime is tied to the iterator's lifetime.
    // The 'using' inside a yield iterator's body runs on disposal of the enumerator,
    // so early break (Take(N)) correctly disposes the stream and cursor.
    //
    // CONTRACT: each yielded ExcelRow is valid only until the next MoveNext() call.
    // The cursor reuses a single pooled buffer — do not store a row or its Cells span
    // across loop iterations. Use .Select(r => r.CloneRow()).ToList() if independent
    // copies are needed.
    private IEnumerable<ExcelRow> StreamRangeCore(Stream sheetStream, ExcelRange range, ReadMode mode)
    {
        using var s = sheetStream;
        using var cursor = XlsxSheetScanner.OpenCursor(
            s, _sharedStrings.Value, _styles.Value,
            _metadata.UsesDate1904, mode, range);

        while (cursor.MoveNext())
        {
            yield return cursor.Current;
        }
    }

    public async IAsyncEnumerable<ExcelRow> StreamRangeAsync(
        string sheetName,
        ExcelRange range,
        ReadMode mode,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct)
    {
        ThrowIfDisposed();
        ct.ThrowIfCancellationRequested();

        var sheet = FindSheet(sheetName);

        // The 'using' inside an async iterator method is safe — the stream stays alive
        // until the async enumerator is disposed (end of 'await foreach' or cancellation).
        using var sheetStream = OpenSheetStream(sheet.Path);
        using var cursor = XlsxSheetScanner.OpenCursor(
            sheetStream, _sharedStrings.Value, _styles.Value,
            _metadata.UsesDate1904, mode, range);

        // Outer-async / inner-sync loop: parse from the already-loaded buffer without
        // blocking, and only await I/O at buffer boundaries.
        while (!ct.IsCancellationRequested)
        {
            // Inner-sync: parse as many rows as the buffer holds (no I/O, no await).
            if (cursor.TryParseNext(out var row))
            {
                yield return row;
                continue;
            }

            // Sheet data fully consumed (</sheetData> found) — stop immediately.
            if (cursor.IsSheetDone) { break; }

            // Buffer exhausted — await a true async refill here (the only await point).
            bool hasMore = await cursor.RefillAsync(ct).ConfigureAwait(false);
            if (!hasMore) { break; }
        }

        ct.ThrowIfCancellationRequested();
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _package.Dispose();
        _disposed = true;
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        await _package.DisposeAsync().ConfigureAwait(false);
        _disposed = true;
    }

    /// <summary>
    /// Opens a stream for a worksheet entry. Uses a fresh, independent ZipArchive when the
    /// package is file-backed (enabling concurrent calls); falls back to the shared archive
    /// for stream-backed workbooks.
    /// </summary>
    private Stream OpenSheetStream(string sheetPath)
    {
        var freshStream = _package.TryOpenFreshEntry(sheetPath);
        if (freshStream is not null)
        {
            return freshStream;
        }

        var entry = _package.GetEntry(sheetPath)
            ?? throw new MalformedWorkbookException($"Worksheet entry '{sheetPath}' was not found in the package.");
        return entry.OpenBuffered();
    }

    private WorkbookMetadata.WorkbookSheetInfo FindSheet(string sheetName)
    {
        foreach (var sheet in _metadata.Sheets)
        {
            if (string.Equals(sheet.Name, sheetName, StringComparison.OrdinalIgnoreCase))
            {
                return sheet;
            }
        }

        throw new SheetNotFoundException(sheetName);
    }

    private void ThrowIfDisposed()
    {
        if (_disposed)
        {
            ThrowHelpers.ThrowObjectDisposed(nameof(XlsxWorkbookReader));
        }
    }
}
