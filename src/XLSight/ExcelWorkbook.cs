using System.Diagnostics;
using System.Runtime.CompilerServices;
using XLSight.Analysis;
using XLSight.Internal.Analysis;
using XLSight.Internal.Packaging;
using XLSight.Internal.Parsing;
using XLSight.Internal.Readers;
using XLSight.Internal.Readers.Xlsb;
using XLSight.Internal.Readers.Xlsx;
using XLSight.Internal.Vba;

namespace XLSight;

/// <summary>
/// Provides synchronous and asynchronous access to an Excel workbook (.xlsx, .xlsm, .xlsb).
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

    /// <summary>Gets the workbook container format.</summary>
    public WorkbookFormat Format => _engine.Format;

    /// <summary>Gets a value indicating whether the workbook contains VBA macros.</summary>
    public bool HasMacros => _engine.HasMacros;

    /// <summary>Parses and returns source-free VBA project metadata for macro-enabled Open XML workbooks.</summary>
    /// <returns>VBA project metadata, or <see langword="null"/> when the workbook has no VBA project.</returns>
    /// <exception cref="ObjectDisposedException">Thrown when this instance has been disposed.</exception>
    /// <exception cref="InvalidDataException">Thrown when the VBA project exists but cannot be parsed.</exception>
    public VbaProjectInfo? GetVbaProject()
    {
        ThrowIfDisposed();
        EnterOperation();
        try
        {
            return _engine.GetVbaProject();
        }
        catch (VbaProjectParseException ex)
        {
            throw new InvalidDataException("The VBA project could not be parsed.", ex);
        }
        finally
        {
            ExitOperation();
        }
    }

    /// <summary>Reads and decodes a VBA module source by module name.</summary>
    /// <param name="moduleName">The module name declared in the VBA project metadata.</param>
    /// <returns>The decoded module source.</returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="moduleName"/> is null, empty, or whitespace.</exception>
    /// <exception cref="ObjectDisposedException">Thrown when this instance has been disposed.</exception>
    /// <exception cref="InvalidOperationException">Thrown when the workbook has no VBA project.</exception>
    /// <exception cref="InvalidDataException">Thrown when the VBA project or requested module cannot be parsed.</exception>
    public string GetVbaModuleSource(string moduleName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(moduleName);
        ThrowIfDisposed();
        EnterOperation();
        try
        {
            return _engine.GetVbaModuleSource(moduleName);
        }
        catch (VbaProjectParseException ex)
        {
            throw new InvalidDataException("The VBA module source could not be read.", ex);
        }
        finally
        {
            ExitOperation();
        }
    }

    /// <summary>Reads decompressed raw VBA module source bytes by module name.</summary>
    /// <param name="moduleName">The module name declared in the VBA project metadata.</param>
    /// <returns>The decompressed raw module source bytes.</returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="moduleName"/> is null, empty, or whitespace.</exception>
    /// <exception cref="ObjectDisposedException">Thrown when this instance has been disposed.</exception>
    /// <exception cref="InvalidOperationException">Thrown when the workbook has no VBA project.</exception>
    /// <exception cref="InvalidDataException">Thrown when the VBA project or requested module cannot be parsed.</exception>
    public byte[] GetVbaModuleSourceBytes(string moduleName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(moduleName);
        ThrowIfDisposed();
        EnterOperation();
        try
        {
            return _engine.GetVbaModuleSourceBytes(moduleName);
        }
        catch (VbaProjectParseException ex)
        {
            throw new InvalidDataException("The VBA module source could not be read.", ex);
        }
        finally
        {
            ExitOperation();
        }
    }

    /// <summary>Opens a workbook from a seekable stream synchronously.</summary>
    /// <param name="stream">A seekable, readable stream containing the .xlsx or .xlsm file.</param>
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
    /// <param name="filePath">Path to the .xlsx, .xlsm, or .xlsb file.</param>
    /// <returns>A new <see cref="ExcelWorkbook"/> instance.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="filePath"/> is null.</exception>
    public static ExcelWorkbook Open(string filePath)
    {
        ArgumentNullException.ThrowIfNull(filePath);
        XLSightEventSource.Log.WorkbookOpened(filePath);
        WorkbookFormat format = GetFormatFromPath(filePath);
        var fileStream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read);
        return Create(fileStream, ownsStream: true, format);
    }

    /// <summary>Opens a workbook from a stream asynchronously.</summary>
    /// <param name="stream">A readable stream containing the .xlsx or .xlsm file.</param>
    /// <param name="ct">A cancellation token.</param>
    /// <returns>A task that returns a new <see cref="ExcelWorkbook"/> instance.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="stream"/> is null.</exception>
    public static Task<ExcelWorkbook> OpenAsync(Stream stream, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(stream);
        return OpenAsyncCore(stream, ct);
    }

    /// <summary>Opens a workbook from a file path asynchronously.</summary>
    /// <param name="filePath">Path to the .xlsx, .xlsm, or .xlsb file.</param>
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
        ct.ThrowIfCancellationRequested();
        return CreateFromPackageSync(package);
    }

    private static async Task<ExcelWorkbook> OpenFileAsyncCore(string filePath, CancellationToken ct)
    {
        XLSightEventSource.Log.WorkbookOpened(filePath);
        WorkbookFormat format = GetFormatFromPath(filePath);
        var fileStream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read);
        var package = await XlsxPackage.OpenAsync(fileStream, ownsStream: true, ct).ConfigureAwait(false);
        ct.ThrowIfCancellationRequested();
        return CreateFromPackageSync(package, format);
    }

    private static ExcelWorkbook Create(
        Stream stream,
        bool ownsStream = false,
        WorkbookFormat format = WorkbookFormat.Xlsx)
    {
        var package = XlsxPackage.Open(stream, ownsStream: ownsStream);
        return CreateFromPackageSync(package, format);
    }

    private static ExcelWorkbook CreateFromPackageSync(
        XlsxPackage package,
        WorkbookFormat format = WorkbookFormat.Xlsx)
    {
        WorkbookFormat effectiveFormat = DetectPackageFormat(package, format);
        if (effectiveFormat == WorkbookFormat.Xlsb)
        {
            return CreateXlsbFromPackage(package);
        }

        using var workbookStream = package.GetEntry("xl/workbook.xml")!.OpenBuffered();
        using var relsStream = package.GetEntry("xl/_rels/workbook.xml.rels")!.OpenBuffered();
        bool hasMacros = package.GetEntry("xl/vbaProject.bin") is not null;
        var def = WorkbookParser.Parse(workbookStream, hasMacros);
        var metadata = RelationshipsParser.Parse(relsStream, def);

        // SST and styles are loaded lazily inside the engine on first use.
        var engine = new XlsxWorkbookReader(package, metadata, effectiveFormat);
        return new ExcelWorkbook(engine);
    }

    private static WorkbookFormat DetectPackageFormat(XlsxPackage package, WorkbookFormat format)
    {
        if (format == WorkbookFormat.Xlsb)
        {
            return WorkbookFormat.Xlsb;
        }

        bool hasWorkbookBin = package.GetEntry("xl/workbook.bin") is not null;
        bool hasWorkbookXml = package.GetEntry("xl/workbook.xml") is not null;
        return hasWorkbookBin || !hasWorkbookXml ? WorkbookFormat.Xlsb : format;
    }

    private static WorkbookFormat GetFormatFromPath(string filePath)
    {
        string extension = Path.GetExtension(filePath);
        if (string.Equals(extension, ".xlsm", StringComparison.OrdinalIgnoreCase))
        {
            return WorkbookFormat.Xlsm;
        }

        if (string.Equals(extension, ".xlsb", StringComparison.OrdinalIgnoreCase))
        {
            return WorkbookFormat.Xlsb;
        }

        return WorkbookFormat.Xlsx;
    }

    private static ExcelWorkbook CreateXlsbFromPackage(XlsxPackage package)
    {
        var workbookEntry = package.GetEntry("xl/workbook.bin")
            ?? throw new MalformedWorkbookException("XLSB workbook metadata entry 'xl/workbook.bin' was not found.");
        var relsEntry = package.GetEntry("xl/_rels/workbook.bin.rels")
            ?? throw new MalformedWorkbookException("XLSB workbook relationships entry 'xl/_rels/workbook.bin.rels' was not found.");

        // Both streams feed XlsbRecordIterator which pools its own 64 KB buffer,
        // so OpenBuffered() would add a redundant heap allocation.
        using Stream workbookStream = workbookEntry.Open();
        using Stream relsStream = relsEntry.Open();
        var rels = PackageRelationshipReader.Read(relsStream, "xl/workbook.bin");
        var relationshipPaths = rels.ToDictionary(
            kvp => kvp.Key,
            kvp => kvp.Value.Target,
            StringComparer.Ordinal);
        var metadata = XlsbWorkbookParser.Parse(workbookStream, relationshipPaths);
        return new ExcelWorkbook(new XlsbWorkbookReader(package, metadata));
    }

    // ── ReadCell (string overloads) ──────────────────────────────────────────

    /// <summary>Reads a single cell value synchronously.</summary>
    /// <param name="sheet">The sheet name.</param>
    /// <param name="cellAddress">The cell address, e.g. "A1". Case-insensitive.</param>
    /// <param name="mode">Whether to return cached values or formula text.</param>
    /// <returns>The decoded cell value.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="sheet"/> or <paramref name="cellAddress"/> is null.</exception>
    /// <exception cref="ObjectDisposedException">Thrown when this instance has been disposed.</exception>
    /// <exception cref="SheetNotFoundException">Thrown when the sheet does not exist.</exception>
    /// <exception cref="InvalidAddressException">Thrown when the address cannot be parsed or is a range.</exception>
    public ExcelCellValue ReadCell(string sheet, string cellAddress, ReadMode mode = ReadMode.Values)
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
            return _engine.ReadCell(sheet, ExcelAddress.Parse(cellAddress), mode);
        }
        finally
        {
            ExitOperation();
        }
    }

    /// <summary>Reads a single cell value synchronously using a typed address.</summary>
    /// <param name="sheet">The sheet name.</param>
    /// <param name="address">The cell address.</param>
    /// <param name="mode">Whether to return cached values or formula text.</param>
    /// <returns>The decoded cell value.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="sheet"/> is null.</exception>
    /// <exception cref="ObjectDisposedException">Thrown when this instance has been disposed.</exception>
    /// <exception cref="SheetNotFoundException">Thrown when the sheet does not exist.</exception>
    public ExcelCellValue ReadCell(string sheet, ExcelAddress address, ReadMode mode = ReadMode.Values)
    {
        ArgumentNullException.ThrowIfNull(sheet);
        ThrowIfDisposed();
        EnterOperation();
        using var activity = ActivitySource.StartActivity("ReadCell");
        activity?.SetTag("sheet", sheet);
        activity?.SetTag("cell", address.ToString());
        try
        {
            return _engine.ReadCell(sheet, address, mode);
        }
        finally
        {
            ExitOperation();
        }
    }

    // ── ReadRange (string overloads) ─────────────────────────────────────────

    /// <summary>Reads a rectangular range of cells synchronously.</summary>
    /// <param name="sheet">The sheet name.</param>
    /// <param name="rangeAddress">The range address, e.g. "A1:D10". Case-insensitive.</param>
    /// <param name="mode">Whether to return cached values or formula text.</param>
    /// <returns>The range result containing all cell values.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="sheet"/> or <paramref name="rangeAddress"/> is null.</exception>
    /// <exception cref="ObjectDisposedException">Thrown when this instance has been disposed.</exception>
    /// <exception cref="SheetNotFoundException">Thrown when the sheet does not exist.</exception>
    /// <exception cref="InvalidAddressException">Thrown when the address cannot be parsed.</exception>
    /// <exception cref="RangeTooLargeException">Thrown when the range exceeds the cell limit.</exception>
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
            var range = AddressParser.Parse(rangeAddress.ToUpperInvariant().AsSpan());
            return _engine.ReadRange(sheet, range, mode);
        }
        finally
        {
            XLSightEventSource.Log.ReadRangeStop();
            ExitOperation();
        }
    }

    /// <summary>Reads a rectangular range of cells synchronously using a typed range.</summary>
    /// <param name="sheet">The sheet name.</param>
    /// <param name="range">The range to read.</param>
    /// <param name="mode">Whether to return cached values or formula text.</param>
    /// <returns>The range result containing all cell values.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="sheet"/> is null.</exception>
    /// <exception cref="ObjectDisposedException">Thrown when this instance has been disposed.</exception>
    /// <exception cref="SheetNotFoundException">Thrown when the sheet does not exist.</exception>
    /// <exception cref="RangeTooLargeException">Thrown when the range is unbounded or exceeds the cell limit.</exception>
    public RangeResult ReadRange(string sheet, ExcelRange range, ReadMode mode = ReadMode.Values)
    {
        ArgumentNullException.ThrowIfNull(sheet);
        ThrowIfDisposed();
        EnterOperation();
        XLSightEventSource.Log.ReadRangeStart(sheet, range.ToString());
        using var activity = ActivitySource.StartActivity("ReadRange");
        activity?.SetTag("sheet", sheet);
        try
        {
            return _engine.ReadRange(sheet, range, mode);
        }
        finally
        {
            XLSightEventSource.Log.ReadRangeStop();
            ExitOperation();
        }
    }

    // ── ReadCellAsync ────────────────────────────────────────────────────────

    /// <summary>Reads a single cell value asynchronously.</summary>
    /// <param name="sheet">The sheet name.</param>
    /// <param name="cellAddress">The cell address, e.g. "A1". Case-insensitive.</param>
    /// <param name="mode">Whether to return cached values or formula text.</param>
    /// <param name="ct">A cancellation token.</param>
    /// <returns>A task that returns the decoded cell value.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="sheet"/> or <paramref name="cellAddress"/> is null.</exception>
    /// <exception cref="ObjectDisposedException">Thrown when this instance has been disposed.</exception>
    /// <exception cref="SheetNotFoundException">Thrown when the sheet does not exist.</exception>
    /// <exception cref="InvalidAddressException">Thrown when the address cannot be parsed or is a range.</exception>
    public async Task<ExcelCellValue> ReadCellAsync(
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
            return await _engine.ReadCellAsync(sheet, ExcelAddress.Parse(cellAddress), mode, ct).ConfigureAwait(false);
        }
        finally
        {
            ExitOperation();
        }
    }

    /// <summary>Reads a single cell value asynchronously using a typed address.</summary>
    /// <param name="sheet">The sheet name.</param>
    /// <param name="address">The cell address.</param>
    /// <param name="mode">Whether to return cached values or formula text.</param>
    /// <param name="ct">A cancellation token.</param>
    /// <returns>A task that returns the decoded cell value.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="sheet"/> is null.</exception>
    /// <exception cref="ObjectDisposedException">Thrown when this instance has been disposed.</exception>
    /// <exception cref="SheetNotFoundException">Thrown when the sheet does not exist.</exception>
    public async Task<ExcelCellValue> ReadCellAsync(
        string sheet,
        ExcelAddress address,
        ReadMode mode = ReadMode.Values,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(sheet);
        ThrowIfDisposed();
        EnterOperation();
        using var activity = ActivitySource.StartActivity("ReadCell");
        activity?.SetTag("sheet", sheet);
        activity?.SetTag("cell", address.ToString());
        try
        {
            return await _engine.ReadCellAsync(sheet, address, mode, ct).ConfigureAwait(false);
        }
        finally
        {
            ExitOperation();
        }
    }

    // ── ReadRangeAsync ───────────────────────────────────────────────────────

    /// <summary>Reads a rectangular range of cells asynchronously.</summary>
    /// <param name="sheet">The sheet name.</param>
    /// <param name="rangeAddress">The range address, e.g. "A1:D10". Case-insensitive.</param>
    /// <param name="mode">Whether to return cached values or formula text.</param>
    /// <param name="ct">A cancellation token.</param>
    /// <returns>A task that returns the range result.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="sheet"/> or <paramref name="rangeAddress"/> is null.</exception>
    /// <exception cref="ObjectDisposedException">Thrown when this instance has been disposed.</exception>
    /// <exception cref="SheetNotFoundException">Thrown when the sheet does not exist.</exception>
    /// <exception cref="InvalidAddressException">Thrown when the address cannot be parsed.</exception>
    /// <exception cref="RangeTooLargeException">Thrown when the range exceeds the cell limit.</exception>
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
        XLSightEventSource.Log.ReadRangeStart(sheet, rangeAddress);
        using var activity = ActivitySource.StartActivity("ReadRange");
        activity?.SetTag("sheet", sheet);
        activity?.SetTag("range", rangeAddress);
        try
        {
            var range = AddressParser.Parse(rangeAddress.ToUpperInvariant().AsSpan());
            return await _engine.ReadRangeAsync(sheet, range, mode, ct).ConfigureAwait(false);
        }
        finally
        {
            XLSightEventSource.Log.ReadRangeStop();
            ExitOperation();
        }
    }

    /// <summary>Reads a rectangular range of cells asynchronously using a typed range.</summary>
    /// <param name="sheet">The sheet name.</param>
    /// <param name="range">The range to read.</param>
    /// <param name="mode">Whether to return cached values or formula text.</param>
    /// <param name="ct">A cancellation token.</param>
    /// <returns>A task that returns the range result.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="sheet"/> is null.</exception>
    /// <exception cref="ObjectDisposedException">Thrown when this instance has been disposed.</exception>
    /// <exception cref="SheetNotFoundException">Thrown when the sheet does not exist.</exception>
    /// <exception cref="RangeTooLargeException">Thrown when the range is unbounded or exceeds the cell limit.</exception>
    public async Task<RangeResult> ReadRangeAsync(
        string sheet,
        ExcelRange range,
        ReadMode mode = ReadMode.Values,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(sheet);
        ThrowIfDisposed();
        EnterOperation();
        XLSightEventSource.Log.ReadRangeStart(sheet, range.ToString());
        using var activity = ActivitySource.StartActivity("ReadRange");
        activity?.SetTag("sheet", sheet);
        try
        {
            return await _engine.ReadRangeAsync(sheet, range, mode, ct).ConfigureAwait(false);
        }
        finally
        {
            XLSightEventSource.Log.ReadRangeStop();
            ExitOperation();
        }
    }

    // ── Analyze ──────────────────────────────────────────────────────────────

    /// <summary>Analyzes all sheets in the workbook and returns structural information.</summary>
    /// <param name="level">The analysis depth to execute.</param>
    /// <param name="maxDegreeOfParallelism">
    /// Maximum number of sheets to scan concurrently. Pass <c>1</c> to force sequential execution
    /// (useful in thread-pool-constrained environments such as heavily loaded ASP.NET Core servers).
    /// Pass <c>-1</c> (default) to let the library choose automatically based on CPU count.
    /// Only applies when the workbook was opened from a file path.
    /// </param>
    /// <param name="options">Analysis tuning options, or <see langword="null"/> for defaults.</param>
    /// <returns>A workbook info object describing all sheets, named ranges, and workbook properties.</returns>
    /// <exception cref="ObjectDisposedException">Thrown when this instance has been disposed.</exception>
    public WorkbookInfo Analyze(
        AnalysisLevel level = AnalysisLevel.Full,
        int maxDegreeOfParallelism = -1,
        AnalysisOptions? options = null)
    {
        ThrowIfDisposed();
        EnterOperation();
        using var activity = ActivitySource.StartActivity("Analyze");
        activity?.SetTag("level", level.ToString());
        try
        {
            return _engine.Analyze(level, maxDegreeOfParallelism, options);
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
    public Task<WorkbookInfo> AnalyzeAsync(CancellationToken ct)
        => AnalyzeAsync(AnalysisLevel.Full, ct: ct);

    /// <summary>Analyzes all sheets in the workbook asynchronously and returns structural information.</summary>
    /// <param name="level">The analysis depth to execute.</param>
    /// <param name="maxDegreeOfParallelism">
    /// Maximum number of sheets to scan concurrently. Pass <c>1</c> to force sequential execution
    /// (useful in thread-pool-constrained environments such as heavily loaded ASP.NET Core servers).
    /// Pass <c>-1</c> (default) to let the library choose automatically. Only applies when the
    /// workbook was opened from a file path.
    /// </param>
    /// <param name="options">Analysis tuning options, or <see langword="null"/> for defaults.</param>
    /// <param name="ct">A cancellation token.</param>
    /// <returns>A task that returns a workbook info object.</returns>
    /// <exception cref="ObjectDisposedException">Thrown when this instance has been disposed.</exception>
    public async Task<WorkbookInfo> AnalyzeAsync(
        AnalysisLevel level = AnalysisLevel.Full,
        int maxDegreeOfParallelism = -1,
        AnalysisOptions? options = null,
        CancellationToken ct = default)
    {
        ThrowIfDisposed();
        EnterOperation();
        using var activity = ActivitySource.StartActivity("Analyze");
        activity?.SetTag("level", level.ToString());
        try
        {
            return await _engine.AnalyzeAsync(level, maxDegreeOfParallelism, options, ct).ConfigureAwait(false);
        }
        finally
        {
            ExitOperation();
        }
    }

    /// <summary>Analyzes a single sheet and returns its structural information.</summary>
    /// <param name="sheet">The sheet name.</param>
    /// <param name="level">The analysis depth to execute.</param>
    /// <param name="options">Analysis tuning options, or <see langword="null"/> for defaults.</param>
    /// <returns>A sheet info object describing columns, merged regions, tables, and inferred headers.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="sheet"/> is null.</exception>
    /// <exception cref="ObjectDisposedException">Thrown when this instance has been disposed.</exception>
    /// <exception cref="SheetNotFoundException">Thrown when the sheet does not exist.</exception>
    public SheetInfo AnalyzeSheet(
        string sheet,
        AnalysisLevel level = AnalysisLevel.Full,
        AnalysisOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(sheet);
        ThrowIfDisposed();
        EnterOperation();
        XLSightEventSource.Log.AnalyzeSheetStart(sheet);
        using var activity = ActivitySource.StartActivity("AnalyzeSheet");
        activity?.SetTag("sheet", sheet);
        activity?.SetTag("level", level.ToString());
        try
        {
            return _engine.AnalyzeSheet(sheet, level, options);
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
    /// <exception cref="SheetNotFoundException">Thrown when the sheet does not exist.</exception>
    public Task<SheetInfo> AnalyzeSheetAsync(string sheet, CancellationToken ct)
        => AnalyzeSheetAsync(sheet, AnalysisLevel.Full, options: null, ct);

    /// <summary>Analyzes a single sheet asynchronously and returns its structural information.</summary>
    /// <param name="sheet">The sheet name.</param>
    /// <param name="level">The analysis depth to execute.</param>
    /// <param name="options">Analysis tuning options, or <see langword="null"/> for defaults.</param>
    /// <param name="ct">A cancellation token.</param>
    /// <returns>A task that returns a sheet info object.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="sheet"/> is null.</exception>
    /// <exception cref="ObjectDisposedException">Thrown when this instance has been disposed.</exception>
    /// <exception cref="SheetNotFoundException">Thrown when the sheet does not exist.</exception>
    public async Task<SheetInfo> AnalyzeSheetAsync(
        string sheet,
        AnalysisLevel level = AnalysisLevel.Full,
        AnalysisOptions? options = null,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(sheet);
        ThrowIfDisposed();
        EnterOperation();
        XLSightEventSource.Log.AnalyzeSheetStart(sheet);
        using var activity = ActivitySource.StartActivity("AnalyzeSheet");
        activity?.SetTag("sheet", sheet);
        activity?.SetTag("level", level.ToString());
        try
        {
            return await _engine.AnalyzeSheetAsync(sheet, level, options, ct).ConfigureAwait(false);
        }
        finally
        {
            XLSightEventSource.Log.AnalyzeSheetStop();
            ExitOperation();
        }
    }

    // ── Borrowed row readers ──────────────────────────────────────────────────

    /// <summary>
    /// Opens a forward-only borrowed row reader for the specified sheet.
    /// The row returned by <see cref="ExcelSheetReader.Current"/> is only valid until the
    /// next successful call to <see cref="ExcelSheetReader.Read"/> or
    /// <see cref="ExcelSheetReader.ReadAsync"/>.
    /// </summary>
    /// <param name="sheet">The sheet name.</param>
    /// <param name="mode">Whether to return cached values or formula text.</param>
    /// <returns>A borrowed row reader for the sheet.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="sheet"/> is null.</exception>
    /// <exception cref="ObjectDisposedException">Thrown when this instance has been disposed.</exception>
    /// <exception cref="SheetNotFoundException">Thrown when the sheet does not exist.</exception>
    public ExcelSheetReader GetSheetReader(string sheet, ReadMode mode = ReadMode.Values)
    {
        ArgumentNullException.ThrowIfNull(sheet);
        return GetRangeReaderCore(sheet, ExcelRange.Unbounded, mode);
    }

    /// <summary>
    /// Opens a forward-only borrowed row reader for the specified range.
    /// The row returned by <see cref="ExcelSheetReader.Current"/> is only valid until the
    /// next successful call to <see cref="ExcelSheetReader.Read"/> or
    /// <see cref="ExcelSheetReader.ReadAsync"/>.
    /// </summary>
    /// <param name="sheet">The sheet name.</param>
    /// <param name="range">The range address, e.g. "A1:D10". Case-insensitive.</param>
    /// <param name="mode">Whether to return cached values or formula text.</param>
    /// <returns>A borrowed row reader for the range.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="sheet"/> or <paramref name="range"/> is null.</exception>
    /// <exception cref="ObjectDisposedException">Thrown when this instance has been disposed.</exception>
    /// <exception cref="SheetNotFoundException">Thrown when the sheet does not exist.</exception>
    /// <exception cref="InvalidAddressException">Thrown when the address cannot be parsed.</exception>
    public ExcelSheetReader GetRangeReader(string sheet, string range, ReadMode mode = ReadMode.Values)
    {
        ArgumentNullException.ThrowIfNull(sheet);
        ArgumentNullException.ThrowIfNull(range);
        return GetRangeReaderCore(sheet, ExcelRange.Parse(range), mode);
    }

    /// <summary>
    /// Opens a forward-only borrowed row reader for the specified typed range.
    /// The row returned by <see cref="ExcelSheetReader.Current"/> is only valid until the
    /// next successful call to <see cref="ExcelSheetReader.Read"/> or
    /// <see cref="ExcelSheetReader.ReadAsync"/>.
    /// </summary>
    /// <param name="sheet">The sheet name.</param>
    /// <param name="range">The range to read.</param>
    /// <param name="mode">Whether to return cached values or formula text.</param>
    /// <returns>A borrowed row reader for the range.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="sheet"/> is null.</exception>
    /// <exception cref="ObjectDisposedException">Thrown when this instance has been disposed.</exception>
    /// <exception cref="SheetNotFoundException">Thrown when the sheet does not exist.</exception>
    public ExcelSheetReader GetRangeReader(string sheet, ExcelRange range, ReadMode mode = ReadMode.Values)
    {
        ArgumentNullException.ThrowIfNull(sheet);
        return GetRangeReaderCore(sheet, range, mode);
    }

    /// <summary>Opens a borrowed row reader for the specified sheet.</summary>
    public ValueTask<ExcelSheetReader> GetSheetReaderAsync(
        string sheet,
        ReadMode mode = ReadMode.Values,
        CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        return new(GetSheetReader(sheet, mode));
    }

    /// <summary>Opens a borrowed row reader for the specified range.</summary>
    public ValueTask<ExcelSheetReader> GetRangeReaderAsync(
        string sheet,
        string range,
        ReadMode mode = ReadMode.Values,
        CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        return new(GetRangeReader(sheet, range, mode));
    }

    /// <summary>Opens a borrowed row reader for the specified typed range.</summary>
    public ValueTask<ExcelSheetReader> GetRangeReaderAsync(
        string sheet,
        ExcelRange range,
        ReadMode mode = ReadMode.Values,
        CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        return new(GetRangeReader(sheet, range, mode));
    }

    // ── StreamSheetAsync / StreamRangeAsync ──────────────────────────────────

    /// <summary>
    /// Streams all rows of a sheet asynchronously.
    /// Each yielded row is an independent snapshot safe to buffer, materialize, or pass to LINQ.
    /// </summary>
    /// <param name="sheet">The sheet name.</param>
    /// <param name="mode">Whether to return cached values or formula text.</param>
    /// <param name="ct">A cancellation token.</param>
    /// <returns>An async sequence of <see cref="ExcelRow"/> objects.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="sheet"/> is null.</exception>
    /// <exception cref="ObjectDisposedException">Thrown when this instance has been disposed.</exception>
    /// <exception cref="SheetNotFoundException">Thrown when the sheet does not exist.</exception>
    public IAsyncEnumerable<ExcelRow> StreamSheetAsync(
        string sheet,
        ReadMode mode = ReadMode.Values,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(sheet);
        return StreamRangeAsync(sheet, ExcelRange.Unbounded, mode, ct);
    }

    /// <summary>
    /// Streams a range of rows asynchronously.
    /// Each yielded row is an independent snapshot safe to buffer, materialize, or pass to LINQ.
    /// </summary>
    /// <param name="sheet">The sheet name.</param>
    /// <param name="range">The range address, e.g. "A1:D10". Case-insensitive.</param>
    /// <param name="mode">Whether to return cached values or formula text.</param>
    /// <param name="ct">A cancellation token.</param>
    /// <returns>An async sequence of <see cref="ExcelRow"/> objects within the range.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="sheet"/> or <paramref name="range"/> is null.</exception>
    /// <exception cref="ObjectDisposedException">Thrown when this instance has been disposed.</exception>
    /// <exception cref="SheetNotFoundException">Thrown when the sheet does not exist.</exception>
    /// <exception cref="InvalidAddressException">Thrown when the address cannot be parsed.</exception>
    public IAsyncEnumerable<ExcelRow> StreamRangeAsync(
        string sheet,
        string range,
        ReadMode mode = ReadMode.Values,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(sheet);
        ArgumentNullException.ThrowIfNull(range);
        return StreamRangeAsync(sheet, ExcelRange.Parse(range), mode, ct);
    }

    /// <summary>
    /// Streams a range of rows asynchronously using a typed range.
    /// Each yielded row is an independent snapshot safe to buffer, materialize, or pass to LINQ.
    /// </summary>
    /// <param name="sheet">The sheet name.</param>
    /// <param name="range">The range to stream.</param>
    /// <param name="mode">Whether to return cached values or formula text.</param>
    /// <param name="ct">A cancellation token.</param>
    /// <returns>An async sequence of <see cref="ExcelRow"/> objects within the range.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="sheet"/> is null.</exception>
    /// <exception cref="ObjectDisposedException">Thrown when this instance has been disposed.</exception>
    /// <exception cref="SheetNotFoundException">Thrown when the sheet does not exist.</exception>
    public async IAsyncEnumerable<ExcelRow> StreamRangeAsync(
        string sheet,
        ExcelRange range,
        ReadMode mode = ReadMode.Values,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(sheet);
        var reader = await GetRangeReaderAsync(sheet, range, mode, ct).ConfigureAwait(false);
        await using (reader.ConfigureAwait(false))
        {
            while (await reader.ReadAsync(ct).ConfigureAwait(false))
            {
                yield return reader.Current.ToSnapshot();
            }
        }
    }

    // ── StreamSheet / StreamRange ────────────────────────────────────────────

    /// <summary>
    /// Streams all rows of a sheet synchronously.
    /// Each yielded row is an independent snapshot safe to buffer, materialize, or pass to LINQ.
    /// </summary>
    /// <param name="sheet">The sheet name.</param>
    /// <param name="mode">Whether to return cached values or formula text.</param>
    /// <returns>A sequence of <see cref="ExcelRow"/> objects.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="sheet"/> is null.</exception>
    /// <exception cref="ObjectDisposedException">Thrown when this instance has been disposed.</exception>
    /// <exception cref="SheetNotFoundException">Thrown when the sheet does not exist.</exception>
    public IEnumerable<ExcelRow> StreamSheet(string sheet, ReadMode mode = ReadMode.Values)
    {
        ArgumentNullException.ThrowIfNull(sheet);
        return StreamRange(sheet, ExcelRange.Unbounded, mode);
    }

    /// <summary>
    /// Streams a range of rows synchronously.
    /// Each yielded row is an independent snapshot safe to buffer, materialize, or pass to LINQ.
    /// </summary>
    /// <param name="sheet">The sheet name.</param>
    /// <param name="range">The range address, e.g. "A1:D10". Case-insensitive.</param>
    /// <param name="mode">Whether to return cached values or formula text.</param>
    /// <returns>A sequence of <see cref="ExcelRow"/> objects within the range.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="sheet"/> or <paramref name="range"/> is null.</exception>
    /// <exception cref="ObjectDisposedException">Thrown when this instance has been disposed.</exception>
    /// <exception cref="SheetNotFoundException">Thrown when the sheet does not exist.</exception>
    /// <exception cref="InvalidAddressException">Thrown when the address cannot be parsed.</exception>
    public IEnumerable<ExcelRow> StreamRange(
        string sheet,
        string range,
        ReadMode mode = ReadMode.Values)
    {
        ArgumentNullException.ThrowIfNull(sheet);
        ArgumentNullException.ThrowIfNull(range);
        return StreamRange(sheet, ExcelRange.Parse(range), mode);
    }

    /// <summary>
    /// Streams a range of rows synchronously using a typed range.
    /// Each yielded row is an independent snapshot safe to buffer, materialize, or pass to LINQ.
    /// </summary>
    /// <param name="sheet">The sheet name.</param>
    /// <param name="range">The range to stream.</param>
    /// <param name="mode">Whether to return cached values or formula text.</param>
    /// <returns>A sequence of <see cref="ExcelRow"/> objects within the range.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="sheet"/> is null.</exception>
    /// <exception cref="ObjectDisposedException">Thrown when this instance has been disposed.</exception>
    /// <exception cref="SheetNotFoundException">Thrown when the sheet does not exist.</exception>
    public IEnumerable<ExcelRow> StreamRange(
        string sheet,
        ExcelRange range,
        ReadMode mode = ReadMode.Values)
    {
        ArgumentNullException.ThrowIfNull(sheet);
        return StreamRangeCore(sheet, range, mode);
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

    private void ExitOperation()
    {
        if (!_engine.IsFileBacked)
        {
            Volatile.Write(ref _busy, 0);
        }
    }

    /// <summary>
    /// Opens a borrowed row reader whose cells outside <paramref name="projection"/> keep
    /// their position but are never materialized. Used by the XLSight.Query package to skip
    /// value decoding (chiefly shared-string resolution) for columns a query never reads.
    /// </summary>
    internal ExcelSheetReader GetRangeReader(string sheet, ExcelRange range, ReadMode mode, RowProjection? projection)
        => GetRangeReaderCore(sheet, range, mode, projection);

    private ExcelSheetReader GetRangeReaderCore(string sheet, ExcelRange range, ReadMode mode, RowProjection? projection = null)
    {
        ThrowIfDisposed();
        EnterOperation();
        try
        {
            return new ExcelSheetReader(_engine.OpenCursor(sheet, range, mode, projection), ExitOperation);
        }
        catch
        {
            ExitOperation();
            throw;
        }
    }

    private IEnumerable<ExcelRow> StreamRangeCore(string sheet, ExcelRange range, ReadMode mode)
    {
        using var reader = GetRangeReader(sheet, range, mode);
        while (reader.Read())
        {
            yield return reader.Current.ToSnapshot();
        }
    }

    private void ThrowIfDisposed()
    {
        if (_disposed)
        {
            ThrowHelpers.ThrowObjectDisposed(nameof(ExcelWorkbook));
        }
    }
}
