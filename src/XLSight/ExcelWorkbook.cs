using System.Diagnostics;
using XLSight.Internal.Readers;
using XLSight.Internal.Readers.Xlsx;
using XLSight.Internal.Packaging;
using XLSight.Models;
using XLSight.Internal.Parsing;

namespace XLSight;

/// <summary>
/// Provides synchronous and asynchronous access to an Excel workbook (.xlsx).
/// Supports reading cell values, ranges, streaming rows, and analyzing sheet structure.
/// </summary>
public sealed class ExcelWorkbook : IDisposable, IAsyncDisposable
{
    private static readonly ActivitySource ActivitySource = new("XLSight", "0.1.0");

    private readonly IWorkbookReader _engine;
    private bool _disposed;
    private int _busy;

    private ExcelWorkbook(IWorkbookReader engine)
    {
        _engine = engine;
    }

    /// <summary>Gets the names of all sheets in the workbook, in order.</summary>
    public IReadOnlyList<string> SheetNames => _engine.SheetNames;

    /// <summary>Gets a value indicating whether the workbook uses the 1904 date system.</summary>
    public bool IsDate1904 => _engine.IsDate1904;

    /// <summary>Gets a value indicating whether the workbook contains VBA macros.</summary>
    public bool HasMacros => _engine.HasMacros;

    /// <summary>Opens a workbook from a seekable stream synchronously.</summary>
    /// <param name="stream">A seekable, readable stream containing the .xlsx file.</param>
    /// <returns>A new <see cref="ExcelWorkbook"/> instance.</returns>
    /// <remarks>
    /// If the stream is seekable, it is used directly and must remain open for the workbook's lifetime.
    /// For non-seekable streams, use <see cref="OpenAsync(Stream, CancellationToken)"/> instead.
    /// </remarks>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="stream"/> is null.</exception>
    /// <exception cref="InvalidOperationException">Thrown when <paramref name="stream"/> is not seekable.</exception>
    public static ExcelWorkbook Open(Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);
        if (!stream.CanSeek)
        {
            throw new InvalidOperationException(
                "A seekable stream is required for synchronous Open. Use OpenAsync for non-seekable streams.");
        }

        return Create(stream);
    }

    /// <summary>Opens a workbook from a file path synchronously.</summary>
    /// <param name="filePath">Path to the .xlsx file.</param>
    /// <returns>A new <see cref="ExcelWorkbook"/> instance.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="filePath"/> is null.</exception>
    public static ExcelWorkbook Open(string filePath)
    {
        ArgumentNullException.ThrowIfNull(filePath);
        XLSightEventSource.Log.WorkbookOpened(filePath);
        var fileStream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read);
        return Create(fileStream, ownsStream: true);
    }

    /// <summary>Opens a workbook from a stream asynchronously.</summary>
    /// <param name="stream">A readable stream containing the .xlsx file.</param>
    /// <param name="ct">A cancellation token.</param>
    /// <returns>A task that returns a new <see cref="ExcelWorkbook"/> instance.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="stream"/> is null.</exception>
    public static Task<ExcelWorkbook> OpenAsync(Stream stream, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(stream);
        return OpenAsyncCore(stream, ct);
    }

    /// <summary>Opens a workbook from a file path asynchronously.</summary>
    /// <param name="filePath">Path to the .xlsx file.</param>
    /// <param name="ct">A cancellation token.</param>
    /// <returns>A task that returns a new <see cref="ExcelWorkbook"/> instance.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="filePath"/> is null.</exception>
    public static Task<ExcelWorkbook> OpenAsync(string filePath, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(filePath);
        return OpenFileAsyncCore(filePath, ct);
    }

    private static async Task<ExcelWorkbook> OpenAsyncCore(Stream stream, CancellationToken ct)
    {
        XLSightEventSource.Log.WorkbookOpened("stream");
        var package = await XlsxPackage.OpenAsync(stream, cancellationToken: ct).ConfigureAwait(false);
        return await CreateFromPackageAsync(package, ct).ConfigureAwait(false);
    }

    private static async Task<ExcelWorkbook> OpenFileAsyncCore(string filePath, CancellationToken ct)
    {
        XLSightEventSource.Log.WorkbookOpened(filePath);
        var fileStream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read);
        var package = await XlsxPackage.OpenAsync(fileStream, ownsStream: true, ct).ConfigureAwait(false);
        return await CreateFromPackageAsync(package, ct).ConfigureAwait(false);
    }

    private static ExcelWorkbook Create(Stream stream, bool ownsStream = false)
    {
        var package = XlsxPackage.Open(stream, ownsStream: ownsStream);
        return CreateFromPackageSync(package);
    }

    private static ExcelWorkbook CreateFromPackageSync(XlsxPackage package)
    {
        using var workbookStream = package.GetEntry("xl/workbook.xml")!.OpenBuffered();
        using var relsStream = package.GetEntry("xl/_rels/workbook.xml.rels")!.OpenBuffered();
        var def = WorkbookParser.Parse(workbookStream);
        var metadata = RelationshipsParser.Parse(relsStream, def);

        // SST and styles are loaded lazily inside the engine on first use.
        var engine = new XlsxWorkbookReader(package, metadata);
        return new ExcelWorkbook(engine);
    }

    private static Task<ExcelWorkbook> CreateFromPackageAsync(XlsxPackage package, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        return Task.FromResult(CreateFromPackageSync(package));
    }

    /// <summary>Reads a single cell value synchronously.</summary>
    /// <param name="sheet">The sheet name.</param>
    /// <param name="cellAddress">The cell address, e.g. "A1".</param>
    /// <param name="mode">Whether to return cached values or formula text.</param>
    /// <returns>The cell result containing its value and location.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="sheet"/> or <paramref name="cellAddress"/> is null.</exception>
    /// <exception cref="ObjectDisposedException">Thrown when this instance has been disposed.</exception>
    /// <exception cref="Exceptions.SheetNotFoundException">Thrown when the sheet does not exist.</exception>
    /// <exception cref="Exceptions.InvalidAddressException">Thrown when the address cannot be parsed.</exception>
    public CellResult ReadCell(string sheet, string cellAddress, ReadMode mode = ReadMode.Values)
    {
        ArgumentNullException.ThrowIfNull(sheet);
        ArgumentNullException.ThrowIfNull(cellAddress);
        ThrowIfDisposed();
        EnterOperation();
        using var activity = ActivitySource.StartActivity("ReadCell");
        activity?.SetTag("sheet", sheet);
        activity?.SetTag("cell", cellAddress);
        try
        {
            var range = AddressParser.Parse(cellAddress.AsSpan());
            return _engine.ReadCell(sheet, range.TopLeft, mode);
        }
        finally
        {
            ExitOperation();
        }
    }

    /// <summary>Reads a rectangular range of cells synchronously.</summary>
    /// <param name="sheet">The sheet name.</param>
    /// <param name="rangeAddress">The range address, e.g. "A1:D10".</param>
    /// <param name="mode">Whether to return cached values or formula text.</param>
    /// <returns>The range result containing all cell values.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="sheet"/> or <paramref name="rangeAddress"/> is null.</exception>
    /// <exception cref="ObjectDisposedException">Thrown when this instance has been disposed.</exception>
    /// <exception cref="Exceptions.SheetNotFoundException">Thrown when the sheet does not exist.</exception>
    /// <exception cref="Exceptions.InvalidAddressException">Thrown when the address cannot be parsed.</exception>
    /// <exception cref="Exceptions.RangeTooLargeException">Thrown when the range exceeds the cell limit.</exception>
    public RangeResult ReadRange(string sheet, string rangeAddress, ReadMode mode = ReadMode.Values)
    {
        ArgumentNullException.ThrowIfNull(sheet);
        ArgumentNullException.ThrowIfNull(rangeAddress);
        ThrowIfDisposed();
        EnterOperation();
        XLSightEventSource.Log.ReadRangeStart(sheet, rangeAddress);
        using var activity = ActivitySource.StartActivity("ReadRange");
        activity?.SetTag("sheet", sheet);
        activity?.SetTag("range", rangeAddress);
        try
        {
            var range = AddressParser.Parse(rangeAddress.AsSpan());
            return _engine.ReadRange(sheet, range, mode);
        }
        finally
        {
            XLSightEventSource.Log.ReadRangeStop();
            ExitOperation();
        }
    }

    /// <summary>Reads a single cell value asynchronously.</summary>
    /// <param name="sheet">The sheet name.</param>
    /// <param name="cellAddress">The cell address, e.g. "A1".</param>
    /// <param name="mode">Whether to return cached values or formula text.</param>
    /// <param name="ct">A cancellation token.</param>
    /// <returns>A task that returns the cell result.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="sheet"/> or <paramref name="cellAddress"/> is null.</exception>
    /// <exception cref="ObjectDisposedException">Thrown when this instance has been disposed.</exception>
    /// <exception cref="Exceptions.SheetNotFoundException">Thrown when the sheet does not exist.</exception>
    /// <exception cref="Exceptions.InvalidAddressException">Thrown when the address cannot be parsed.</exception>
    public async Task<CellResult> ReadCellAsync(
        string sheet,
        string cellAddress,
        ReadMode mode = ReadMode.Values,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(sheet);
        ArgumentNullException.ThrowIfNull(cellAddress);
        ThrowIfDisposed();
        EnterOperation();
        using var activity = ActivitySource.StartActivity("ReadCell");
        activity?.SetTag("sheet", sheet);
        activity?.SetTag("cell", cellAddress);
        try
        {
            var range = AddressParser.Parse(cellAddress.AsSpan());
            return await _engine.ReadCellAsync(sheet, range.TopLeft, mode, ct).ConfigureAwait(false);
        }
        finally
        {
            ExitOperation();
        }
    }

    /// <summary>Reads a rectangular range of cells asynchronously.</summary>
    /// <param name="sheet">The sheet name.</param>
    /// <param name="rangeAddress">The range address, e.g. "A1:D10".</param>
    /// <param name="mode">Whether to return cached values or formula text.</param>
    /// <param name="ct">A cancellation token.</param>
    /// <returns>A task that returns the range result.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="sheet"/> or <paramref name="rangeAddress"/> is null.</exception>
    /// <exception cref="ObjectDisposedException">Thrown when this instance has been disposed.</exception>
    /// <exception cref="Exceptions.SheetNotFoundException">Thrown when the sheet does not exist.</exception>
    /// <exception cref="Exceptions.InvalidAddressException">Thrown when the address cannot be parsed.</exception>
    /// <exception cref="Exceptions.RangeTooLargeException">Thrown when the range exceeds the cell limit.</exception>
    public async Task<RangeResult> ReadRangeAsync(
        string sheet,
        string rangeAddress,
        ReadMode mode = ReadMode.Values,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(sheet);
        ArgumentNullException.ThrowIfNull(rangeAddress);
        ThrowIfDisposed();
        EnterOperation();
        using var activity = ActivitySource.StartActivity("ReadRange");
        activity?.SetTag("sheet", sheet);
        activity?.SetTag("range", rangeAddress);
        try
        {
            var range = AddressParser.Parse(rangeAddress.AsSpan());
            return await _engine.ReadRangeAsync(sheet, range, mode, ct).ConfigureAwait(false);
        }
        finally
        {
            ExitOperation();
        }
    }

    /// <summary>Analyzes all sheets in the workbook and returns structural information.</summary>
    /// <returns>A workbook info object describing all sheets, named ranges, and workbook properties.</returns>
    /// <exception cref="ObjectDisposedException">Thrown when this instance has been disposed.</exception>
    public Models.Analysis.WorkbookInfo Analyze()
    {
        ThrowIfDisposed();
        EnterOperation();
        using var activity = ActivitySource.StartActivity("Analyze");
        try
        {
            return _engine.Analyze();
        }
        finally
        {
            ExitOperation();
        }
    }

    /// <summary>Analyzes all sheets in the workbook asynchronously and returns structural information.</summary>
    /// <param name="ct">A cancellation token.</param>
    /// <returns>A task that returns a workbook info object.</returns>
    /// <exception cref="ObjectDisposedException">Thrown when this instance has been disposed.</exception>
    public async Task<Models.Analysis.WorkbookInfo> AnalyzeAsync(CancellationToken ct = default)
    {
        ThrowIfDisposed();
        EnterOperation();
        using var activity = ActivitySource.StartActivity("Analyze");
        try
        {
            return await _engine.AnalyzeAsync(ct).ConfigureAwait(false);
        }
        finally
        {
            ExitOperation();
        }
    }

    /// <summary>Analyzes a single sheet and returns its structural information.</summary>
    /// <param name="sheet">The sheet name.</param>
    /// <returns>A sheet info object describing columns, merged regions, tables, and inferred headers.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="sheet"/> is null.</exception>
    /// <exception cref="ObjectDisposedException">Thrown when this instance has been disposed.</exception>
    /// <exception cref="Exceptions.SheetNotFoundException">Thrown when the sheet does not exist.</exception>
    public Models.Analysis.SheetInfo AnalyzeSheet(string sheet)
    {
        ArgumentNullException.ThrowIfNull(sheet);
        ThrowIfDisposed();
        EnterOperation();
        XLSightEventSource.Log.AnalyzeSheetStart(sheet);
        using var activity = ActivitySource.StartActivity("AnalyzeSheet");
        activity?.SetTag("sheet", sheet);
        try
        {
            return _engine.AnalyzeSheet(sheet);
        }
        finally
        {
            XLSightEventSource.Log.AnalyzeSheetStop();
            ExitOperation();
        }
    }

    /// <summary>Analyzes a single sheet asynchronously and returns its structural information.</summary>
    /// <param name="sheet">The sheet name.</param>
    /// <param name="ct">A cancellation token.</param>
    /// <returns>A task that returns a sheet info object.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="sheet"/> is null.</exception>
    /// <exception cref="ObjectDisposedException">Thrown when this instance has been disposed.</exception>
    /// <exception cref="Exceptions.SheetNotFoundException">Thrown when the sheet does not exist.</exception>
    public async Task<Models.Analysis.SheetInfo> AnalyzeSheetAsync(string sheet, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(sheet);
        ThrowIfDisposed();
        EnterOperation();
        using var activity = ActivitySource.StartActivity("AnalyzeSheet");
        activity?.SetTag("sheet", sheet);
        try
        {
            return await _engine.AnalyzeSheetAsync(sheet, ct).ConfigureAwait(false);
        }
        finally
        {
            ExitOperation();
        }
    }

    /// <summary>Streams all rows of a sheet asynchronously without buffering the entire sheet.</summary>
    /// <param name="sheet">The sheet name.</param>
    /// <param name="mode">Whether to return cached values or formula text.</param>
    /// <param name="ct">A cancellation token.</param>
    /// <returns>An async sequence of <see cref="ExcelRow"/> objects.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="sheet"/> is null.</exception>
    /// <exception cref="ObjectDisposedException">Thrown when this instance has been disposed.</exception>
    /// <exception cref="Exceptions.SheetNotFoundException">Thrown when the sheet does not exist.</exception>
    public IAsyncEnumerable<ExcelRow> StreamSheetAsync(
        string sheet,
        ReadMode mode = ReadMode.Values,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(sheet);
        ThrowIfDisposed();
        // Known limitation: the busy lock is not held across async enumeration.
        // Concurrent access during iteration is the caller's responsibility.
        return _engine.StreamRangeAsync(sheet, ExcelRange.Unbounded, mode, ct);
    }

    /// <summary>Streams a range of rows asynchronously without buffering.</summary>
    /// <param name="sheet">The sheet name.</param>
    /// <param name="range">The range address, e.g. "A1:D10".</param>
    /// <param name="mode">Whether to return cached values or formula text.</param>
    /// <param name="ct">A cancellation token.</param>
    /// <returns>An async sequence of <see cref="ExcelRow"/> objects within the range.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="sheet"/> or <paramref name="range"/> is null.</exception>
    /// <exception cref="ObjectDisposedException">Thrown when this instance has been disposed.</exception>
    /// <exception cref="Exceptions.SheetNotFoundException">Thrown when the sheet does not exist.</exception>
    /// <exception cref="Exceptions.InvalidAddressException">Thrown when the address cannot be parsed.</exception>
    public IAsyncEnumerable<ExcelRow> StreamRangeAsync(
        string sheet,
        string range,
        ReadMode mode = ReadMode.Values,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(sheet);
        ArgumentNullException.ThrowIfNull(range);
        ThrowIfDisposed();
        // Known limitation: the busy lock is not held across async enumeration.
        // Concurrent access during iteration is the caller's responsibility.
        var parsedRange = AddressParser.Parse(range.AsSpan());
        return _engine.StreamRangeAsync(sheet, parsedRange, mode, ct);
    }

    /// <summary>Streams all rows of a sheet synchronously without buffering the entire sheet.</summary>
    /// <param name="sheet">The sheet name.</param>
    /// <param name="mode">Whether to return cached values or formula text.</param>
    /// <returns>A sequence of <see cref="ExcelRow"/> objects.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="sheet"/> is null.</exception>
    /// <exception cref="ObjectDisposedException">Thrown when this instance has been disposed.</exception>
    /// <exception cref="Exceptions.SheetNotFoundException">Thrown when the sheet does not exist.</exception>
    public IEnumerable<ExcelRow> StreamSheet(string sheet, ReadMode mode = ReadMode.Values)
    {
        ArgumentNullException.ThrowIfNull(sheet);
        ThrowIfDisposed();
        EnterOperation();
        try
        {
            return _engine.StreamRange(sheet, ExcelRange.Unbounded, mode);
        }
        finally
        {
            ExitOperation();
        }
    }

    /// <summary>Streams a range of rows synchronously without buffering.</summary>
    /// <param name="sheet">The sheet name.</param>
    /// <param name="range">The range address, e.g. "A1:D10".</param>
    /// <param name="mode">Whether to return cached values or formula text.</param>
    /// <returns>A sequence of <see cref="ExcelRow"/> objects within the range.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="sheet"/> or <paramref name="range"/> is null.</exception>
    /// <exception cref="ObjectDisposedException">Thrown when this instance has been disposed.</exception>
    /// <exception cref="Exceptions.SheetNotFoundException">Thrown when the sheet does not exist.</exception>
    /// <exception cref="Exceptions.InvalidAddressException">Thrown when the address cannot be parsed.</exception>
    public IEnumerable<ExcelRow> StreamRange(
        string sheet,
        string range,
        ReadMode mode = ReadMode.Values)
    {
        ArgumentNullException.ThrowIfNull(sheet);
        ArgumentNullException.ThrowIfNull(range);
        ThrowIfDisposed();
        EnterOperation();
        try
        {
            var parsedRange = AddressParser.Parse(range.AsSpan());
            return _engine.StreamRange(sheet, parsedRange, mode);
        }
        finally
        {
            ExitOperation();
        }
    }

    /// <summary>Releases all resources used by this workbook.</summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        XLSightEventSource.Log.WorkbookDisposed();
        _engine.Dispose();
    }

    /// <summary>Asynchronously releases all resources used by this workbook.</summary>
    /// <returns>A task representing the asynchronous dispose operation.</returns>
    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        XLSightEventSource.Log.WorkbookDisposed();
        await _engine.DisposeAsync().ConfigureAwait(false);
    }

    private void EnterOperation()
    {
        // File-backed workbooks open a fresh ZipArchive per sheet read, so concurrent
        // operations are safe. Only serialize for stream-backed workbooks where the
        // shared ZipArchive is not thread-safe for concurrent entry reads.
        if (_engine.IsFileBacked)
        {
            return;
        }

        if (Interlocked.CompareExchange(ref _busy, 1, 0) != 0)
        {
            throw new InvalidOperationException("ExcelWorkbook does not support concurrent operations.");
        }
    }

    private void ExitOperation() => Volatile.Write(ref _busy, 0);

    private void ThrowIfDisposed()
    {
        if (_disposed)
        {
            ThrowHelpers.ThrowObjectDisposed(nameof(ExcelWorkbook));
        }
    }
}
