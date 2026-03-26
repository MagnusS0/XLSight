using System.IO.Compression;
using XLSight.ByteEngine;
using XLSight.Exceptions;
using XLSight.Infrastructure;
using XLSight.Models;
using XLSight.Models.Analysis;
using XLSight.Packaging;
using XLSight.SharedStrings;
using XLSight.Styles;
using XLSight.Worksheets;

namespace XLSight.Engines;

internal sealed class XlsxWorkbookEngine : IWorkbookEngine
{
    private readonly XlsxPackage _package;
    private readonly WorkbookMetadata _metadata;
    private readonly Lazy<string[]> _sharedStrings;
    private readonly Lazy<StyleTable> _styles;
    private readonly XlsxNameTable _names;
    private volatile bool _disposed;

    internal XlsxWorkbookEngine(XlsxPackage package, WorkbookMetadata metadata, XlsxNameTable names)
    {
        _package  = package;
        _metadata = metadata;
        _names    = names;
        _sharedStrings = new Lazy<string[]>(LoadSharedStrings, LazyThreadSafetyMode.ExecutionAndPublication);
        _styles        = new Lazy<StyleTable>(LoadStyles,       LazyThreadSafetyMode.ExecutionAndPublication);
    }

    private string[] LoadSharedStrings()
    {
        var entry = _package.GetEntry("xl/sharedStrings.xml");
        if (entry is null)
        {
            return [];
        }

        using var stream = entry.OpenBuffered();
        return SharedStringsParser.Parse(stream, _names);
    }

    private StyleTable LoadStyles()
    {
        var entry = _package.GetEntry("xl/styles.xml");
        if (entry is null)
        {
            return StyleTable.Default;
        }

        using var stream = entry.OpenBuffered();
        return StylesParser.Parse(stream, _names);
    }

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

    public ExcelRangeResult ReadRange(string sheetName, ExcelRange range, ExcelReadMode mode)
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

        var entry = _package.GetEntry(sheet.Path);
        if (entry is null)
        {
            throw new MalformedWorkbookException($"Worksheet entry '{sheet.Path}' was not found in the package.");
        }

        using var sheetStream = entry.OpenBuffered();

        var buffer = new ExcelCellValue[cellCount];
        var sink = new RangeReadSink(range, buffer, _sharedStrings.Value, _styles.Value, _metadata.UsesDate1904, mode);
        WorksheetScanner.Scan(sheetStream, _names, ref sink);

        return new ExcelRangeResult
        {
            Sheet = sheetName,
            StartRow = range.TopLeft.Row,
            StartColumn = range.TopLeft.Column,
            Width = range.Width,
            Height = range.Height,
            Cells = buffer,
        };
    }

    public ExcelCellResult ReadCell(string sheetName, ExcelAddress address, ExcelReadMode mode)
    {
        ThrowIfDisposed();

        var range = new ExcelRange(address, address);
        var result = ReadRange(sheetName, range, mode);

        return new ExcelCellResult
        {
            Sheet = sheetName,
            Row = address.Row,
            Column = address.Column,
            Value = result.Cells[0],
        };
    }

    public async Task<ExcelCellResult> ReadCellAsync(string sheetName, ExcelAddress address, ExcelReadMode mode, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var range = new ExcelRange(address, address);
        var result = await ReadRangeAsync(sheetName, range, mode, ct).ConfigureAwait(false);
        return new ExcelCellResult
        {
            Sheet = sheetName,
            Row = address.Row,
            Column = address.Column,
            Value = result.Cells[0],
        };
    }

    public async Task<ExcelRangeResult> ReadRangeAsync(string sheetName, ExcelRange range, ExcelReadMode mode, CancellationToken ct)
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

        var entry = _package.GetEntry(sheet.Path)
            ?? throw new MalformedWorkbookException($"Worksheet entry '{sheet.Path}' was not found in the package.");

        using var sheetStream = entry.OpenBuffered();
        var buffer = new ExcelCellValue[cellCount];
        var wrapper = new RangeReadSinkWrapper(range, buffer, _sharedStrings.Value, _styles.Value, _metadata.UsesDate1904, mode);
        await WorksheetScanner.ScanAsync(sheetStream, _names, wrapper, ct).ConfigureAwait(false);

        return new ExcelRangeResult
        {
            Sheet = sheetName,
            StartRow = range.TopLeft.Row,
            StartColumn = range.TopLeft.Column,
            Width = range.Width,
            Height = range.Height,
            Cells = buffer,
        };
    }

    public ExcelWorkbookInfo Analyze()
    {
        ThrowIfDisposed();

        var sheets = _metadata.Sheets
            .Select((s, i) => AnalyzeSheetCore(s, i))
            .ToList();

        var namedRanges = _metadata.NamedRanges
            .Select(nr => new ExcelNamedRange
            {
                Name = nr.Name,
                Sheet = nr.ScopeSheetName,
                Reference = nr.Reference,
            })
            .ToList();

        return new ExcelWorkbookInfo
        {
            Sheets = sheets,
            NamedRanges = namedRanges,
            HasMacros = _metadata.HasMacros,
            IsDate1904 = _metadata.UsesDate1904,
            AnalyzedAtUtc = DateTimeOffset.UtcNow,
        };
    }

    public ExcelSheetInfo AnalyzeSheet(string sheetName)
    {
        ThrowIfDisposed();

        var sheet = FindSheet(sheetName);
        int sheetIndex = _metadata.Sheets
            .Select((s, i) => (s, i))
            .First(t => string.Equals(t.s.Name, sheetName, StringComparison.OrdinalIgnoreCase))
            .i;

        return AnalyzeSheetCore(sheet, sheetIndex);
    }

    public async Task<ExcelWorkbookInfo> AnalyzeAsync(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        ThrowIfDisposed();

        var sheets = new List<ExcelSheetInfo>();
        foreach (var sheet in _metadata.Sheets)
        {
            ct.ThrowIfCancellationRequested();
            var sheetInfo = await AnalyzeSheetAsync(sheet.Name, ct).ConfigureAwait(false);
            sheets.Add(sheetInfo);
        }

        var namedRanges = _metadata.NamedRanges
            .Select(nr => new ExcelNamedRange
            {
                Name = nr.Name,
                Sheet = nr.ScopeSheetName,
                Reference = nr.Reference,
            })
            .ToArray();

        return new ExcelWorkbookInfo
        {
            Sheets = sheets,
            NamedRanges = namedRanges,
            HasMacros = _metadata.HasMacros,
            IsDate1904 = _metadata.UsesDate1904,
            AnalyzedAtUtc = DateTimeOffset.UtcNow,
        };
    }

    public async Task<ExcelSheetInfo> AnalyzeSheetAsync(string sheetName, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        ThrowIfDisposed();

        var sheet = FindSheet(sheetName);
        int sheetIndex = _metadata.Sheets
            .Select((s, i) => (s, i))
            .First(t => string.Equals(t.s.Name, sheetName, StringComparison.OrdinalIgnoreCase))
            .i;

        var entry = _package.GetEntry(sheet.Path)
            ?? throw new MalformedWorkbookException($"Worksheet entry '{sheet.Path}' was not found in the package.");

        using var sheetStream = entry.OpenBuffered();
        var sink = new AnalysisSinkWrapper(_sharedStrings.Value, _styles.Value, _metadata.UsesDate1904, ExcelReadMode.Values);
        await WorksheetScanner.ScanAsync(sheetStream, _names, sink, ct).ConfigureAwait(false);
        return sink.Build(sheetName, sheetIndex, []);
    }

    private ExcelSheetInfo AnalyzeSheetCore(WorkbookMetadata.WorkbookSheetInfo sheet, int sheetIndex)
    {
        var entry = _package.GetEntry(sheet.Path);
        if (entry is null)
        {
            throw new MalformedWorkbookException($"Worksheet entry '{sheet.Path}' was not found in the package.");
        }

        using var sheetStream = entry.OpenBuffered();

        var sink = new AnalysisSink(_sharedStrings.Value, _styles.Value, _metadata.UsesDate1904, ExcelReadMode.Values);
        WorksheetScanner.Scan(sheetStream, _names, ref sink);

        return sink.Build(sheet.Name, sheetIndex, []);
    }

    public IEnumerable<ExcelRow> StreamRange(string sheetName, ExcelRange range, ExcelReadMode mode)
    {
        ThrowIfDisposed();

        var sheet = FindSheet(sheetName);
        var entry = _package.GetEntry(sheet.Path)
            ?? throw new MalformedWorkbookException($"Worksheet entry '{sheet.Path}' was not found in the package.");

        // Validation runs eagerly here; iteration is deferred to the private iterator below,
        // which owns the stream lifetime via 'using' inside the iterator body.
        return StreamRangeCore(entry, range, mode);
    }

    // Private iterator — stream lifetime is tied to the iterator's lifetime.
    // The 'using' inside a yield iterator's body runs on disposal of the enumerator,
    // so early break (Take(N)) correctly disposes the stream and cursor.
    //
    // CONTRACT: each yielded ExcelRow is valid only until the next MoveNext() call.
    // The cursor reuses a single pooled buffer — do not store a row or its Cells span
    // across loop iterations. Use .Select(r => r.CloneRow()).ToList() if independent
    // copies are needed.
    private IEnumerable<ExcelRow> StreamRangeCore(ZipArchiveEntry entry, ExcelRange range, ExcelReadMode mode)
    {
        using var sheetStream = entry.OpenBuffered();
        using var cursor = XlsxSheetScanner.OpenCursor(
            sheetStream, _sharedStrings.Value, _styles.Value,
            _metadata.UsesDate1904, mode, range);

        while (cursor.MoveNext())
        {
            yield return cursor.Current;
        }
    }

    public async IAsyncEnumerable<ExcelRow> StreamRangeAsync(
        string sheetName,
        ExcelRange range,
        ExcelReadMode mode,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct)
    {
        ThrowIfDisposed();
        ct.ThrowIfCancellationRequested();

        var sheet = FindSheet(sheetName);
        var entry = _package.GetEntry(sheet.Path)
            ?? throw new MalformedWorkbookException($"Worksheet entry '{sheet.Path}' was not found in the package.");

        // The 'using' inside an async iterator method is safe — the stream stays alive
        // until the async enumerator is disposed (end of 'await foreach' or cancellation).
        using var sheetStream = entry.OpenBuffered();
        using var cursor = XlsxSheetScanner.OpenCursor(
            sheetStream, _sharedStrings.Value, _styles.Value,
            _metadata.UsesDate1904, mode, range);

        while (!ct.IsCancellationRequested && cursor.MoveNext())
        {
            yield return cursor.Current;
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
            Infrastructure.ThrowHelpers.ThrowObjectDisposed(nameof(XlsxWorkbookEngine));
        }
    }
}
