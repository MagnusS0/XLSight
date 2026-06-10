using XLSight.Internal.Analysis;
using XLSight.Internal.Metadata;
using XLSight.Internal.Packaging;
using XLSight.Internal.Sinks;
using XLSight.Internal.Vba;
using XLSight.Analysis;

namespace XLSight.Internal.Readers.Xlsx;

internal sealed class XlsxWorkbookReader : IWorkbookReader
{
    private readonly XlsxPackage _package;
    private readonly WorkbookMetadata _metadata;
    private readonly WorkbookFormat _format;
    private readonly Lazy<SharedStringTable> _sharedStrings;
    private readonly Lazy<StyleTable> _styles;
    private readonly Lazy<AnalyzerMetadata> _analyzerMetadata;
    private readonly Lazy<string[]> _sheetNames;
    private volatile bool _disposed;

    internal XlsxWorkbookReader(XlsxPackage package, WorkbookMetadata metadata, WorkbookFormat format = WorkbookFormat.Xlsx)
    {
        _package = package;
        _metadata = metadata;
        _format = format;
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

    public WorkbookFormat Format
    {
        get
        {
            ThrowIfDisposed();
            return _format;
        }
    }

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

    public VbaProjectInfo? GetVbaProject()
    {
        ThrowIfDisposed();
        if (!_metadata.HasMacros)
        {
            return null;
        }

        using var stream = OpenVbaProjectStream();
        return stream is null ? null : VbaProjectParser.Parse(stream);
    }

    public string GetVbaModuleSource(string moduleName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(moduleName);
        ThrowIfDisposed();

        using var stream = OpenRequiredVbaProjectStream();
        return VbaProjectParser.ReadModuleSource(stream, moduleName);
    }

    public byte[] GetVbaModuleSourceBytes(string moduleName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(moduleName);
        ThrowIfDisposed();

        using var stream = OpenRequiredVbaProjectStream();
        return VbaProjectParser.ReadModuleSourceBytes(stream, moduleName);
    }

    public RangeResult ReadRange(string sheetName, ExcelRange range, ReadMode mode)
    {
        ThrowIfDisposed();
        return ReadRangeCore(sheetName, range, mode);
    }

    public ExcelCellValue ReadCell(string sheetName, ExcelAddress address, ReadMode mode)
    {
        ThrowIfDisposed();

        var range = new ExcelRange(address, address);
        var result = ReadRange(sheetName, range, mode);
        return result.Cells.Span[0];
    }

    public async Task<ExcelCellValue> ReadCellAsync(string sheetName, ExcelAddress address, ReadMode mode, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var range = new ExcelRange(address, address);
        var result = await ReadRangeAsync(sheetName, range, mode, ct).ConfigureAwait(false);
        return result.Cells.Span[0];
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

        using var cursor = OpenCursor(sheetName, range, mode);
        while (!ct.IsCancellationRequested)
        {
            if (cursor.TryParseNext(out var row))
            {
                int rowOffset = (row.RowIndex - startRow) * width;
                for (int c = startCol; c <= endCol; c++)
                {
                    buffer[rowOffset + (c - startCol)] = row.GetCell(c);
                }

                continue;
            }

            if (cursor.IsSheetDone) { break; }

            bool hasMore = await cursor.RefillAsync(ct).ConfigureAwait(false);
            if (!hasMore) { break; }
        }

        ct.ThrowIfCancellationRequested();

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

    public IRowCursor OpenCursor(string sheetName, ExcelRange range, ReadMode mode)
    {
        ThrowIfDisposed();

        var (sheet, _) = FindSheetWithIndex(sheetName);
        var sheetStream = OpenSheetStream(sheet.Path);
        var cursor = XlsxSheetScanner.OpenCursor(
            sheetStream,
            _sharedStrings.Value,
            _styles.Value,
            _metadata.UsesDate1904,
            mode,
            range);
        return new OwnedRowCursor(sheetStream, cursor);
    }

    public WorkbookInfo Analyze(AnalysisLevel level, int maxDegreeOfParallelism = -1, AnalysisOptions? options = null)
    {
        ThrowIfDisposed();

        AnalyzerMetadata analysisMetadata = _analyzerMetadata.Value;
        int dop = ResolveSheetDop(maxDegreeOfParallelism);
        var sheets = _package.IsFileBacked && dop > 1
            ? AnalyzeSheetsParallel(analysisMetadata, level, options, dop)
            : _metadata.Sheets.Select((s, i) => AnalyzeSheetCore(s, i, analysisMetadata, level, options)).ToList();

        return new WorkbookInfo
        {
            Level = level,
            Sheets = sheets,
            Exact = analysisMetadata.WorkbookExact,
            AnalyzedAtUtc = DateTimeOffset.UtcNow,
        };
    }

    private List<SheetInfo> AnalyzeSheetsParallel(AnalyzerMetadata analysisMetadata, AnalysisLevel level, AnalysisOptions? options, int dop)
    {
        var results = new SheetInfo[_metadata.Sheets.Count];
        Parallel.For(0, _metadata.Sheets.Count,
            new ParallelOptions { MaxDegreeOfParallelism = dop },
            i => results[i] = AnalyzeSheetCore(_metadata.Sheets[i], i, analysisMetadata, level, options));
        return [.. results];
    }

    private int ResolveSheetDop(int requested)
    {
        int count = _metadata.Sheets.Count;
        if (requested == 1 || count <= 1) { return 1; }
        if (requested <= 0) { return Math.Min(Environment.ProcessorCount, count); }
        return Math.Min(requested, count);
    }

    public SheetInfo AnalyzeSheet(string sheetName, AnalysisLevel level, AnalysisOptions? options = null)
    {
        ThrowIfDisposed();

        var (sheet, sheetIndex) = FindSheetWithIndex(sheetName);
        AnalyzerMetadata analysisMetadata = _analyzerMetadata.Value;

        return AnalyzeSheetCore(sheet, sheetIndex, analysisMetadata, level, options);
    }

    public async Task<WorkbookInfo> AnalyzeAsync(AnalysisLevel level, int maxDegreeOfParallelism = -1, AnalysisOptions? options = null, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        ThrowIfDisposed();

        AnalyzerMetadata analysisMetadata = _analyzerMetadata.Value;
        int dop = ResolveSheetDop(maxDegreeOfParallelism);

        List<SheetInfo> sheets;
        if (_package.IsFileBacked && dop > 1)
        {
            sheets = await AnalyzeSheetsParallelAsync(analysisMetadata, level, options, dop, ct).ConfigureAwait(false);
        }
        else
        {
            sheets = new List<SheetInfo>();
            foreach (var (sheet, i) in _metadata.Sheets.Select((s, i) => (s, i)))
            {
                ct.ThrowIfCancellationRequested();
                sheets.Add(await Task.Run(() => AnalyzeSheetCore(sheet, i, analysisMetadata, level, options), ct).ConfigureAwait(false));
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
        AnalyzerMetadata analysisMetadata, AnalysisLevel level, AnalysisOptions? options, int dop, CancellationToken ct)
    {
        var results = new SheetInfo[_metadata.Sheets.Count];
        await Parallel.ForEachAsync(
            Enumerable.Range(0, _metadata.Sheets.Count),
            new ParallelOptions { MaxDegreeOfParallelism = dop, CancellationToken = ct },
            async (i, ct2) =>
            {
                results[i] = await Task.Run(
                    () => AnalyzeSheetCore(_metadata.Sheets[i], i, analysisMetadata, level, options), ct2).ConfigureAwait(false);
            }).ConfigureAwait(false);
        return [.. results];
    }

    public Task<SheetInfo> AnalyzeSheetAsync(string sheetName, AnalysisLevel level, AnalysisOptions? options, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        ThrowIfDisposed();

        AnalyzerMetadata analysisMetadata = _analyzerMetadata.Value;
        var (sheet, sheetIndex) = FindSheetWithIndex(sheetName);
        return Task.Run(() => AnalyzeSheetCore(sheet, sheetIndex, analysisMetadata, level, options), ct);
    }

    private SheetInfo AnalyzeSheetCore(
        WorkbookMetadata.WorkbookSheetInfo sheet,
        int sheetIndex,
        AnalyzerMetadata analysisMetadata,
        AnalysisLevel level,
        AnalysisOptions? options)
    {
        using var sheetStream = OpenSheetStream(sheet.Path);
        var sink = new AnalysisSink(_sharedStrings.Value, sheet.Name, level, options);
        XlsxSheetScanner.ScanSheet(sheetStream, _sharedStrings.Value, _styles.Value, _metadata.UsesDate1904, ReadMode.Values, ExcelRange.Unbounded, ref sink);
        return sink.Build(sheet.Name, sheetIndex, analysisMetadata.SheetsByPath[sheet.Path], level);
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

    private Stream? OpenVbaProjectStream() => _package.TryOpenEntryBuffered("xl/vbaProject.bin");

    private Stream OpenRequiredVbaProjectStream()
        => OpenVbaProjectStream()
            ?? throw new InvalidOperationException("The workbook does not contain a VBA macro project.");

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
