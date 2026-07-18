using XLSight.Analysis;
using System.Diagnostics.CodeAnalysis;
using XLSight.Internal.Analysis;
using XLSight.Internal.Packaging;
using XLSight.Internal.Scanning;
using XLSight.Internal.Vba;

namespace XLSight.Internal.Readers;

internal abstract class WorkbookReaderBase<
    TSheet,
    [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicParameterlessConstructor)] TSharedStrings>(
    XlsxPackage package,
    WorkbookFormat format,
    IReadOnlyList<TSheet> sheets,
    bool isDate1904) : IWorkbookReader
    where TSharedStrings : class, IDisposable
{
    private Lazy<TSharedStrings> _sharedStrings = null!;
    private readonly SemaphoreSlim _analyzerMetadataGate = new(1, 1);
    private AnalyzerMetadata? _analyzerMetadata;
    private Lazy<string[]> _sheetNames = null!;
    private volatile bool _disposed;

    protected XlsxPackage Package => package;
    protected IReadOnlyList<TSheet> Sheets => sheets;
    protected Lazy<TSharedStrings> SharedStringsLazy => _sharedStrings;
    protected TSharedStrings SharedStrings => _sharedStrings.Value;
    protected AnalyzerMetadata AnalyzerMetadata => GetAnalyzerMetadata(CancellationToken.None);

    public bool IsFileBacked => package.IsFileBacked;

    public WorkbookFormat Format
    {
        get { ThrowIfDisposed(); return format; }
    }

    public IReadOnlyList<string> SheetNames
    {
        get { ThrowIfDisposed(); return _sheetNames.Value; }
    }

    public bool IsDate1904
    {
        get { ThrowIfDisposed(); return isDate1904; }
    }

    public bool HasMacros
    {
        get { ThrowIfDisposed(); return HasMacrosCore(); }
    }

    protected void Initialize()
    {
        _sharedStrings = new Lazy<TSharedStrings>(
            () => LoadSharedStrings(),
            LazyThreadSafetyMode.ExecutionAndPublication);
        _sheetNames = new Lazy<string[]>(
            () => sheets.Select(GetSheetName).ToArray(),
            LazyThreadSafetyMode.PublicationOnly);
    }

    public VbaProjectInfo? GetVbaProject()
    {
        ThrowIfDisposed();
        if (!HasMacrosCore())
        {
            return null;
        }

        using Stream? stream = OpenVbaProjectStream();
        return stream is null ? null : VbaProjectParser.Parse(stream);
    }

    public string GetVbaModuleSource(string moduleName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(moduleName);
        ThrowIfDisposed();
        using Stream stream = OpenRequiredVbaProjectStream();
        return VbaProjectParser.ReadModuleSource(stream, moduleName);
    }

    public byte[] GetVbaModuleSourceBytes(string moduleName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(moduleName);
        ThrowIfDisposed();
        using Stream stream = OpenRequiredVbaProjectStream();
        return VbaProjectParser.ReadModuleSourceBytes(stream, moduleName);
    }

    public ExcelCellValue ReadCell(string sheetName, ExcelAddress address, ReadMode mode)
    {
        var range = new ExcelRange(address, address);
        return ReadRange(sheetName, range, mode).Cells.Span[0];
    }

    public RangeResult ReadRange(string sheetName, ExcelRange range, ReadMode mode)
    {
        ThrowIfDisposed();
        return ReadRangeCore(sheetName, range, mode);
    }

    public Task<ExcelCellValue> ReadCellAsync(
        string sheetName,
        ExcelAddress address,
        ReadMode mode,
        CancellationToken ct)
    {
        Task<RangeResult> task = ReadRangeAsync(sheetName, new ExcelRange(address, address), mode, ct);
        return task.IsCompletedSuccessfully
            ? Task.FromResult(task.Result.Cells.Span[0])
            : AwaitCell(task);

        static async Task<ExcelCellValue> AwaitCell(Task<RangeResult> pending) =>
            (await pending.ConfigureAwait(false)).Cells.Span[0];
    }

    public Task<RangeResult> ReadRangeAsync(
        string sheetName,
        ExcelRange range,
        ReadMode mode,
        CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        ThrowIfDisposed();
        return ReadRangeAsyncCore(sheetName, range, mode, ct);
    }

    public IRowCursor OpenCursor(string sheetName, ExcelRange range, ReadMode mode, RowProjection? projection = null)
    {
        ThrowIfDisposed();
        return OpenCursorCore(FindSheet(sheetName).Sheet, range, mode, projection);
    }

    public void ScanWorksheet<TSink>(string sheetName, ref TSink sink, CancellationToken ct = default)
        where TSink : struct, IWorksheetScanSink
    {
        ct.ThrowIfCancellationRequested();
        ThrowIfDisposed();
        ScanWorksheetCore(FindSheet(sheetName).Sheet, ref sink, ct);
    }

    public WorkbookInfo Analyze(
        AnalysisLevel level,
        int maxDegreeOfParallelism = -1,
        AnalysisOptions? options = null)
    {
        ThrowIfDisposed();
        AnalyzerMetadata metadata = AnalyzerMetadata;
        int dop = ResolveSheetDop(maxDegreeOfParallelism);
        List<SheetInfo> results = package.IsFileBacked && dop > 1
            ? AnalyzeParallel(metadata, level, options, dop)
            : sheets.Select((sheet, index) => AnalyzeSheetCore(
                sheet, index, metadata, level, options, CancellationToken.None)).ToList();
        return BuildWorkbookInfo(level, metadata, results);
    }

    public SheetInfo AnalyzeSheet(string sheetName, AnalysisLevel level, AnalysisOptions? options = null)
    {
        ThrowIfDisposed();
        var (sheet, index) = FindSheet(sheetName);
        return AnalyzeSheetCore(
            sheet, index, AnalyzerMetadata, level, options, CancellationToken.None);
    }

    public async Task<WorkbookInfo> AnalyzeAsync(
        AnalysisLevel level,
        int maxDegreeOfParallelism = -1,
        AnalysisOptions? options = null,
        CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        ThrowIfDisposed();
        AnalyzerMetadata metadata = await Task.Run(
            () => GetAnalyzerMetadata(ct), ct).ConfigureAwait(false);
        int dop = ResolveSheetDop(maxDegreeOfParallelism);
        List<SheetInfo> results = package.IsFileBacked && dop > 1
            ? await AnalyzeParallelAsync(metadata, level, options, dop, ct).ConfigureAwait(false)
            : await AnalyzeSequentialAsync(metadata, level, options, ct).ConfigureAwait(false);
        return BuildWorkbookInfo(level, metadata, results);
    }

    public async Task<SheetInfo> AnalyzeSheetAsync(
        string sheetName,
        AnalysisLevel level,
        AnalysisOptions? options,
        CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        ThrowIfDisposed();
        var (sheet, index) = FindSheet(sheetName);
        return await Task.Run(
            () =>
            {
                AnalyzerMetadata metadata = GetAnalyzerMetadata(ct);
                return AnalyzeSheetCore(sheet, index, metadata, level, options, ct);
            },
            ct).ConfigureAwait(false);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        DisposeSharedStrings();
        package.Dispose();
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        DisposeSharedStrings();
        await package.DisposeAsync().ConfigureAwait(false);
    }

    protected (TSheet Sheet, int Index) FindSheet(string sheetName)
    {
        for (int i = 0; i < sheets.Count; i++)
        {
            if (string.Equals(GetSheetName(sheets[i]), sheetName, StringComparison.OrdinalIgnoreCase))
            {
                return (sheets[i], i);
            }
        }

        throw new SheetNotFoundException(sheetName);
    }

    protected virtual async Task<RangeResult> ReadRangeAsyncCore(
        string sheetName,
        ExcelRange range,
        ReadMode mode,
        CancellationToken ct)
    {
        ExcelCellValue[] buffer = CreateRangeBuffer(range);
        TSheet sheet = FindSheet(sheetName).Sheet;
        using IRowCursor cursor = await Task.Run(
            () => OpenCursorCore(sheet, range, mode),
            ct).ConfigureAwait(false);
        while (!cursor.IsSheetDone)
        {
            ct.ThrowIfCancellationRequested();
            if (cursor.TryParseNext(out ExcelRow row))
            {
                CopyRow(row, range, buffer);
            }
            else if (!await cursor.RefillAsync(ct).ConfigureAwait(false))
            {
                break;
            }
        }

        ct.ThrowIfCancellationRequested();
        return CreateRangeResult(sheetName, range, buffer);
    }

    protected virtual RangeResult ReadRangeCore(string sheetName, ExcelRange range, ReadMode mode)
    {
        ExcelCellValue[] buffer = CreateRangeBuffer(range);
        using IRowCursor cursor = OpenCursorCore(FindSheet(sheetName).Sheet, range, mode);
        while (cursor.MoveNext())
        {
            CopyRow(cursor.Current, range, buffer);
        }

        return CreateRangeResult(sheetName, range, buffer);
    }

    protected abstract string GetSheetName(TSheet sheet);
    protected abstract bool HasMacrosCore();
    protected abstract TSharedStrings LoadSharedStrings();
    protected abstract AnalyzerMetadata BuildAnalyzerMetadata(CancellationToken ct);
    protected abstract IRowCursor OpenCursorCore(TSheet sheet, ExcelRange range, ReadMode mode, RowProjection? projection = null);
    protected abstract void ScanWorksheetCore<TSink>(TSheet sheet, ref TSink sink, CancellationToken ct)
        where TSink : struct, IWorksheetScanSink;
    protected abstract SheetInfo AnalyzeSheetCore(
        TSheet sheet,
        int sheetIndex,
        AnalyzerMetadata metadata,
        AnalysisLevel level,
        AnalysisOptions? options,
        CancellationToken ct);

    private List<SheetInfo> AnalyzeParallel(
        AnalyzerMetadata metadata,
        AnalysisLevel level,
        AnalysisOptions? options,
        int dop)
    {
        var results = new SheetInfo[sheets.Count];
        Parallel.For(
            0,
            sheets.Count,
            new ParallelOptions { MaxDegreeOfParallelism = dop },
            i => results[i] = AnalyzeSheetCore(
                sheets[i], i, metadata, level, options, CancellationToken.None));
        return [.. results];
    }

    private async Task<List<SheetInfo>> AnalyzeParallelAsync(
        AnalyzerMetadata metadata,
        AnalysisLevel level,
        AnalysisOptions? options,
        int dop,
        CancellationToken ct)
    {
        var results = new SheetInfo[sheets.Count];
        await Parallel.ForEachAsync(
            Enumerable.Range(0, sheets.Count),
            new ParallelOptions { MaxDegreeOfParallelism = dop, CancellationToken = ct },
            (i, innerCt) =>
            {
                results[i] = AnalyzeSheetCore(sheets[i], i, metadata, level, options, innerCt);
                return ValueTask.CompletedTask;
            }).ConfigureAwait(false);
        return [.. results];
    }

    private async Task<List<SheetInfo>> AnalyzeSequentialAsync(
        AnalyzerMetadata metadata,
        AnalysisLevel level,
        AnalysisOptions? options,
        CancellationToken ct)
    {
        var results = new List<SheetInfo>(sheets.Count);
        for (int i = 0; i < sheets.Count; i++)
        {
            ct.ThrowIfCancellationRequested();
            int index = i;
            results.Add(await Task.Run(
                () => AnalyzeSheetCore(sheets[index], index, metadata, level, options, ct),
                ct).ConfigureAwait(false));
        }

        return results;
    }

    private int ResolveSheetDop(int requested) =>
        requested == 1 || sheets.Count <= 1
            ? 1
            : Math.Min(requested <= 0 ? Environment.ProcessorCount : requested, sheets.Count);

    private static ExcelCellValue[] CreateRangeBuffer(ExcelRange range)
    {
        long cellCount = range.IsUnbounded ? 0 : (long)range.Width * range.Height;
        if (range.IsUnbounded || cellCount > ExcelLimits.MaxCells)
        {
            throw new RangeTooLargeException(cellCount, ExcelLimits.MaxCells);
        }

        return new ExcelCellValue[cellCount];
    }

    private static void CopyRow(ExcelRow row, ExcelRange range, ExcelCellValue[] buffer)
    {
        int rowOffset = (row.RowIndex - range.TopLeft.Row) * range.Width;
        for (int column = range.TopLeft.Column; column <= range.BottomRight.Column; column++)
        {
            buffer[rowOffset + column - range.TopLeft.Column] = row.GetCell(column);
        }
    }

    private AnalyzerMetadata GetAnalyzerMetadata(CancellationToken ct)
    {
        AnalyzerMetadata? cached = Volatile.Read(ref _analyzerMetadata);
        if (cached is not null)
        {
            return cached;
        }

        _analyzerMetadataGate.Wait(ct);
        try
        {
            cached = _analyzerMetadata;
            if (cached is null)
            {
                cached = BuildAnalyzerMetadata(ct);
                Volatile.Write(ref _analyzerMetadata, cached);
            }

            return cached;
        }
        finally
        {
            _analyzerMetadataGate.Release();
        }
    }

    private static RangeResult CreateRangeResult(string sheetName, ExcelRange range, ExcelCellValue[] buffer) => new()
    {
        Sheet = sheetName,
        StartRow = range.TopLeft.Row,
        StartColumn = range.TopLeft.Column,
        Width = range.Width,
        Height = range.Height,
        Cells = buffer,
    };

    private static WorkbookInfo BuildWorkbookInfo(
        AnalysisLevel level,
        AnalyzerMetadata metadata,
        List<SheetInfo> sheets) => new()
    {
        Level = level,
        Sheets = sheets,
        Exact = metadata.WorkbookExact,
        AnalyzedAtUtc = DateTimeOffset.UtcNow,
    };

    private Stream? OpenVbaProjectStream() => package.TryOpenEntryBuffered("xl/vbaProject.bin");

    private Stream OpenRequiredVbaProjectStream() => OpenVbaProjectStream()
        ?? throw new InvalidOperationException("The workbook does not contain a VBA macro project.");

    private void DisposeSharedStrings()
    {
        if (_sharedStrings.IsValueCreated)
        {
            _sharedStrings.Value.Dispose();
        }
    }

    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(_disposed, GetType());
}
