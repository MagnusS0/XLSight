using XLSight.Exceptions;
using XLSight.Internal.Analysis;
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
    private readonly Lazy<AnalyzerMetadata> _analyzerMetadata;
    private readonly Lazy<string[]> _sheetNames;
    private volatile bool _disposed;

    internal XlsxWorkbookReader(XlsxPackage package, WorkbookMetadata metadata)
    {
        _package = package;
        _metadata = metadata;
        _sharedStrings = new Lazy<SharedStringTable>(LoadSharedStrings, LazyThreadSafetyMode.ExecutionAndPublication);
        _styles = new Lazy<StyleTable>(LoadStyles, LazyThreadSafetyMode.ExecutionAndPublication);
        _analyzerMetadata = new Lazy<AnalyzerMetadata>(
            () => _package.IsFileBacked
                ? AnalyzerMetadataReader.ReadParallel(_package, _metadata)
                : AnalyzerMetadataReader.Read(_package, _metadata),
            LazyThreadSafetyMode.ExecutionAndPublication);
        _sheetNames = new Lazy<string[]>(() => _metadata.Sheets.Select(s => s.Name).ToArray(), LazyThreadSafetyMode.PublicationOnly);
    }

    private SharedStringTable LoadSharedStrings()
    {
        var entry = _package.GetEntry("xl/sharedStrings.xml");
        if (entry is null)
        {
            return SharedStringTable.Empty;
        }

        // Do NOT use 'using' — ownership is transferred to the lazy SharedStringTable,
        // which holds the stream open for on-demand pumping and disposes it when done.
        var stream = entry.OpenBuffered();
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
            return _sheetNames.Value;
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
        return ReadRangeCore(sheetName, range, mode);
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

        if (range.IsUnbounded)
        {
            throw new RangeTooLargeException(0, ExcelLimits.MaxCells);
        }

        long cellCount = (long)range.Width * range.Height;
        if (cellCount > ExcelLimits.MaxCells)
        {
            throw new RangeTooLargeException(cellCount, ExcelLimits.MaxCells);
        }

        int startRow = range.TopLeft.Row;
        int startCol = range.TopLeft.Column;
        int endCol = range.BottomRight.Column;
        int width = range.Width;
        var buffer = new ExcelCellValue[cellCount];

        await foreach (var row in StreamRangeAsync(sheetName, range, mode, ct).ConfigureAwait(false))
        {
            int rowOffset = (row.RowIndex - startRow) * width;
            for (int c = startCol; c <= endCol; c++)
            {
                buffer[rowOffset + (c - startCol)] = row.GetCell(c);
            }
        }

        return new RangeResult
        {
            Sheet = sheetName,
            StartRow = startRow,
            StartColumn = startCol,
            Width = width,
            Height = range.Height,
            Cells = buffer,
        };
    }

    public WorkbookInfo Analyze(AnalysisLevel level, int maxDegreeOfParallelism = -1)
    {
        ThrowIfDisposed();

        AnalyzerMetadata analysisMetadata = _analyzerMetadata.Value;
        int dop = ResolveSheetDop(maxDegreeOfParallelism);
        var sheets = _package.IsFileBacked && dop > 1
            ? AnalyzeSheetsParallel(analysisMetadata, level, dop)
            : _metadata.Sheets.Select((s, i) => AnalyzeSheetCore(s, i, analysisMetadata, level)).ToList();

        return new WorkbookInfo
        {
            Level = level,
            Sheets = sheets,
            Exact = analysisMetadata.WorkbookExact,
            AnalyzedAtUtc = DateTimeOffset.UtcNow,
        };
    }

    private List<SheetInfo> AnalyzeSheetsParallel(AnalyzerMetadata analysisMetadata, AnalysisLevel level, int dop)
    {
        var results = new SheetInfo[_metadata.Sheets.Count];
        Parallel.For(0, _metadata.Sheets.Count,
            new ParallelOptions { MaxDegreeOfParallelism = dop },
            i => results[i] = AnalyzeSheetCore(_metadata.Sheets[i], i, analysisMetadata, level));
        return [.. results];
    }

    private int ResolveSheetDop(int requested)
    {
        int count = _metadata.Sheets.Count;
        if (requested == 1 || count <= 1) { return 1; }
        if (requested <= 0) { return Math.Min(Environment.ProcessorCount, count); }
        return Math.Min(requested, count);
    }

    public SheetInfo AnalyzeSheet(string sheetName, AnalysisLevel level)
    {
        ThrowIfDisposed();

        var (sheet, sheetIndex) = FindSheetWithIndex(sheetName);
        AnalyzerMetadata analysisMetadata = _analyzerMetadata.Value;

        return AnalyzeSheetCore(sheet, sheetIndex, analysisMetadata, level);
    }

    public async Task<WorkbookInfo> AnalyzeAsync(AnalysisLevel level, int maxDegreeOfParallelism = -1, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        ThrowIfDisposed();

        AnalyzerMetadata analysisMetadata = _analyzerMetadata.Value;
        int dop = ResolveSheetDop(maxDegreeOfParallelism);

        List<SheetInfo> sheets;
        if (_package.IsFileBacked && dop > 1)
        {
            sheets = await AnalyzeSheetsParallelAsync(analysisMetadata, level, dop, ct).ConfigureAwait(false);
        }
        else
        {
            sheets = new List<SheetInfo>();
            foreach (var (sheet, i) in _metadata.Sheets.Select((s, i) => (s, i)))
            {
                ct.ThrowIfCancellationRequested();
                sheets.Add(await Task.Run(() => AnalyzeSheetCore(sheet, i, analysisMetadata, level), ct).ConfigureAwait(false));
            }
        }

        return new WorkbookInfo
        {
            Level = level,
            Sheets = sheets,
            Exact = analysisMetadata.WorkbookExact,
            AnalyzedAtUtc = DateTimeOffset.UtcNow,
        };
    }

    private async Task<List<SheetInfo>> AnalyzeSheetsParallelAsync(
        AnalyzerMetadata analysisMetadata, AnalysisLevel level, int dop, CancellationToken ct)
    {
        var results = new SheetInfo[_metadata.Sheets.Count];
        await Parallel.ForEachAsync(
            Enumerable.Range(0, _metadata.Sheets.Count),
            new ParallelOptions { MaxDegreeOfParallelism = dop, CancellationToken = ct },
            async (i, ct2) =>
            {
                results[i] = await Task.Run(
                    () => AnalyzeSheetCore(_metadata.Sheets[i], i, analysisMetadata, level), ct2).ConfigureAwait(false);
            }).ConfigureAwait(false);
        return [.. results];
    }

    public Task<SheetInfo> AnalyzeSheetAsync(string sheetName, AnalysisLevel level, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        ThrowIfDisposed();

        AnalyzerMetadata analysisMetadata = _analyzerMetadata.Value;
        var (sheet, sheetIndex) = FindSheetWithIndex(sheetName);
        return Task.Run(() => AnalyzeSheetCore(sheet, sheetIndex, analysisMetadata, level), ct);
    }

    private SheetInfo AnalyzeSheetCore(
        WorkbookMetadata.WorkbookSheetInfo sheet,
        int sheetIndex,
        AnalyzerMetadata analysisMetadata,
        AnalysisLevel level)
    {
        using var sheetStream = OpenSheetStream(sheet.Path);
        var sink = new AnalysisSink(_sharedStrings.Value, level);
        XlsxSheetScanner.ScanSheet(sheetStream, _sharedStrings.Value, _styles.Value, _metadata.UsesDate1904, ReadMode.Values, ExcelRange.Unbounded, ref sink);
        return sink.Build(sheet.Name, sheetIndex, analysisMetadata.SheetsByPath[sheet.Path], level);
    }

    public IEnumerable<ExcelRow> StreamRange(string sheetName, ExcelRange range, ReadMode mode)
    {
        ThrowIfDisposed();

        var (sheet, _) = FindSheetWithIndex(sheetName);

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

        var (sheet, _) = FindSheetWithIndex(sheetName);

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

        _disposed = true;
        // Dispose SST before package — the SST stream was opened from the package.
        if (_sharedStrings.IsValueCreated) { _sharedStrings.Value.Dispose(); }
        _package.Dispose();
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        if (_sharedStrings.IsValueCreated) { _sharedStrings.Value.Dispose(); }
        await _package.DisposeAsync().ConfigureAwait(false);
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

    private RangeResult ReadRangeCore(string sheetName, ExcelRange range, ReadMode mode)
    {
        var (sheet, _) = FindSheetWithIndex(sheetName);

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

    private (WorkbookMetadata.WorkbookSheetInfo Sheet, int Index) FindSheetWithIndex(string sheetName)
    {
        for (int i = 0; i < _metadata.Sheets.Count; i++)
        {
            var sheet = _metadata.Sheets[i];
            if (string.Equals(sheet.Name, sheetName, StringComparison.OrdinalIgnoreCase))
            {
                return (sheet, i);
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
