using XLSight.Engines;
using XLSight.Infrastructure;
using XLSight.Models;
using XLSight.Packaging;
using XLSight.Parsing;
using XLSight.SharedStrings;
using XLSight.Styles;
using XLSight.Worksheets;

namespace XLSight;

public sealed class ExcelWorkbook : IDisposable, IAsyncDisposable
{
    private readonly IWorkbookEngine _engine;
    private bool _disposed;
    private int _busy;

    private ExcelWorkbook(IWorkbookEngine engine)
    {
        _engine = engine;
    }

    public IReadOnlyList<string> SheetNames => _engine.SheetNames;
    public bool IsDate1904 => _engine.IsDate1904;
    public bool HasMacros => _engine.HasMacros;

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

    public static ExcelWorkbook Open(string filePath)
    {
        ArgumentNullException.ThrowIfNull(filePath);
        using var fileStream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read);
        return Create(fileStream);
    }

    public static Task<ExcelWorkbook> OpenAsync(Stream stream, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(stream);
        return OpenAsyncCore(stream, ct);
    }

    public static Task<ExcelWorkbook> OpenAsync(string filePath, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(filePath);
        return OpenFileAsyncCore(filePath, ct);
    }

    private static async Task<ExcelWorkbook> OpenAsyncCore(Stream stream, CancellationToken ct)
    {
        var package = await XlsxPackage.OpenAsync(stream, ct).ConfigureAwait(false);
        return await CreateFromPackageAsync(package, ct).ConfigureAwait(false);
    }

    private static async Task<ExcelWorkbook> OpenFileAsyncCore(string filePath, CancellationToken ct)
    {
        var fileStream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read);
        await using (fileStream.ConfigureAwait(false))
        {
            var package = await XlsxPackage.OpenAsync(fileStream, ct).ConfigureAwait(false);
            return await CreateFromPackageAsync(package, ct).ConfigureAwait(false);
        }
    }

    private static ExcelWorkbook Create(Stream stream)
    {
        var package = XlsxPackage.Open(stream);
        return CreateFromPackageSync(package);
    }

    private static ExcelWorkbook CreateFromPackageSync(XlsxPackage package)
    {
        var names = new XlsxNameTable();

        using var workbookStream = package.GetEntry("xl/workbook.xml")!.Open();
        using var relsStream = package.GetEntry("xl/_rels/workbook.xml.rels")!.Open();
        var def = WorkbookParser.Parse(workbookStream);
        var metadata = RelationshipsParser.Parse(relsStream, def);

        string[] sharedStrings = [];
        var sstEntry = package.GetEntry("xl/sharedStrings.xml");
        if (sstEntry is not null)
        {
            using var sstStream = sstEntry.Open();
            sharedStrings = SharedStringsParser.Parse(sstStream, names);
        }

        var styles = StyleTable.Default;
        var stylesEntry = package.GetEntry("xl/styles.xml");
        if (stylesEntry is not null)
        {
            using var stylesStream = stylesEntry.Open();
            styles = StylesParser.Parse(stylesStream, names);
        }

        var engine = new XlsxWorkbookEngine(package, metadata, sharedStrings, styles);
        return new ExcelWorkbook(engine);
    }

    private static Task<ExcelWorkbook> CreateFromPackageAsync(XlsxPackage package, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        return Task.FromResult(CreateFromPackageSync(package));
    }

    public ExcelCellResult ReadCell(string sheet, string cellAddress, ExcelReadMode mode = ExcelReadMode.Values)
    {
        ArgumentNullException.ThrowIfNull(sheet);
        ArgumentNullException.ThrowIfNull(cellAddress);
        ThrowIfDisposed();
        EnterOperation();
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

    public ExcelRangeResult ReadRange(string sheet, string rangeAddress, ExcelReadMode mode = ExcelReadMode.Values)
    {
        ArgumentNullException.ThrowIfNull(sheet);
        ArgumentNullException.ThrowIfNull(rangeAddress);
        ThrowIfDisposed();
        EnterOperation();
        try
        {
            var range = AddressParser.Parse(rangeAddress.AsSpan());
            return _engine.ReadRange(sheet, range, mode);
        }
        finally
        {
            ExitOperation();
        }
    }

    public async Task<ExcelCellResult> ReadCellAsync(
        string sheet,
        string cellAddress,
        ExcelReadMode mode = ExcelReadMode.Values,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(sheet);
        ArgumentNullException.ThrowIfNull(cellAddress);
        ThrowIfDisposed();
        EnterOperation();
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

    public async Task<ExcelRangeResult> ReadRangeAsync(
        string sheet,
        string rangeAddress,
        ExcelReadMode mode = ExcelReadMode.Values,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(sheet);
        ArgumentNullException.ThrowIfNull(rangeAddress);
        ThrowIfDisposed();
        EnterOperation();
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

    public Models.Analysis.ExcelWorkbookInfo Analyze()
    {
        ThrowIfDisposed();
        EnterOperation();
        try
        {
            return _engine.Analyze();
        }
        finally
        {
            ExitOperation();
        }
    }

    public async Task<Models.Analysis.ExcelWorkbookInfo> AnalyzeAsync(CancellationToken ct = default)
    {
        ThrowIfDisposed();
        EnterOperation();
        try
        {
            return await _engine.AnalyzeAsync(ct).ConfigureAwait(false);
        }
        finally
        {
            ExitOperation();
        }
    }

    public Models.Analysis.ExcelSheetInfo AnalyzeSheet(string sheet)
    {
        ArgumentNullException.ThrowIfNull(sheet);
        ThrowIfDisposed();
        EnterOperation();
        try
        {
            return _engine.AnalyzeSheet(sheet);
        }
        finally
        {
            ExitOperation();
        }
    }

    public async Task<Models.Analysis.ExcelSheetInfo> AnalyzeSheetAsync(string sheet, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(sheet);
        ThrowIfDisposed();
        EnterOperation();
        try
        {
            return await _engine.AnalyzeSheetAsync(sheet, ct).ConfigureAwait(false);
        }
        finally
        {
            ExitOperation();
        }
    }

    public IAsyncEnumerable<ExcelRow> StreamSheetAsync(
        string sheet,
        ExcelReadMode mode = ExcelReadMode.Values,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(sheet);
        ThrowIfDisposed();
        // Known limitation: the busy lock is not held across async enumeration.
        // Concurrent access during iteration is the caller's responsibility.
        return _engine.StreamRangeAsync(sheet, ExcelRange.Unbounded, mode, ct);
    }

    public IAsyncEnumerable<ExcelRow> StreamRangeAsync(
        string sheet,
        string range,
        ExcelReadMode mode = ExcelReadMode.Values,
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

    public IEnumerable<ExcelRow> StreamSheet(string sheet, ExcelReadMode mode = ExcelReadMode.Values)
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

    public IEnumerable<ExcelRow> StreamRange(
        string sheet,
        string range,
        ExcelReadMode mode = ExcelReadMode.Values)
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

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _engine.Dispose();
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        await _engine.DisposeAsync().ConfigureAwait(false);
    }

    private void EnterOperation()
    {
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
