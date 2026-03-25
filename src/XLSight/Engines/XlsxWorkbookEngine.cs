using XLSight.Exceptions;
using XLSight.Models;
using XLSight.Models.Analysis;
using XLSight.Packaging;
using XLSight.Styles;
using XLSight.Worksheets;

namespace XLSight.Engines;

internal sealed class XlsxWorkbookEngine : IWorkbookEngine
{
    private readonly XlsxPackage _package;
    private readonly WorkbookMetadata _metadata;
    private readonly string[] _sharedStrings;
    private readonly StyleTable _styles;
    private readonly XlsxNameTable _names;
    private bool _disposed;

    internal XlsxWorkbookEngine(
        XlsxPackage package,
        WorkbookMetadata metadata,
        string[] sharedStrings,
        StyleTable styles)
    {
        _package = package;
        _metadata = metadata;
        _sharedStrings = sharedStrings;
        _styles = styles;
        _names = new XlsxNameTable();
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

        using var sheetStream = entry.Open();

        var buffer = new ExcelCellValue[cellCount];
        var sink = new RangeReadSink(range, buffer, _sharedStrings, _styles, _metadata.UsesDate1904, mode);
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

    public Task<ExcelCellResult> ReadCellAsync(string sheetName, ExcelAddress address, ExcelReadMode mode, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        return Task.FromResult(ReadCell(sheetName, address, mode));
    }

    public Task<ExcelRangeResult> ReadRangeAsync(string sheetName, ExcelRange range, ExcelReadMode mode, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        return Task.FromResult(ReadRange(sheetName, range, mode));
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

    public Task<ExcelWorkbookInfo> AnalyzeAsync(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        return Task.FromResult(Analyze());
    }

    public Task<ExcelSheetInfo> AnalyzeSheetAsync(string sheetName, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        return Task.FromResult(AnalyzeSheet(sheetName));
    }

    private ExcelSheetInfo AnalyzeSheetCore(WorkbookMetadata.WorkbookSheetInfo sheet, int sheetIndex)
    {
        var entry = _package.GetEntry(sheet.Path);
        if (entry is null)
        {
            throw new MalformedWorkbookException($"Worksheet entry '{sheet.Path}' was not found in the package.");
        }

        using var sheetStream = entry.Open();

        var sink = new AnalysisSink(_sharedStrings, _styles, _metadata.UsesDate1904, ExcelReadMode.Values);
        WorksheetScanner.Scan(sheetStream, _names, ref sink);

        return sink.Build(sheet.Name, sheetIndex, []);
    }

    public IEnumerable<ExcelRow> StreamRange(string sheetName, ExcelRange range, ExcelReadMode mode)
    {
        ThrowIfDisposed();

        var sheet = FindSheet(sheetName);
        var entry = _package.GetEntry(sheet.Path);
        if (entry is null)
        {
            throw new MalformedWorkbookException($"Worksheet entry '{sheet.Path}' was not found in the package.");
        }

        using var sheetStream = entry.Open();
        var sink = new StreamingSink(range, _sharedStrings, _styles, _metadata.UsesDate1904, mode);
        WorksheetScanner.Scan(sheetStream, _names, ref sink);
        return sink.Rows;
    }

    public IAsyncEnumerable<ExcelRow> StreamRangeAsync(string sheetName, ExcelRange range, ExcelReadMode mode, CancellationToken ct)
    {
        ThrowIfDisposed();
        ct.ThrowIfCancellationRequested();
        var rows = StreamRange(sheetName, range, mode);
        return ToAsyncEnumerable(rows, ct);
    }

    private static async IAsyncEnumerable<ExcelRow> ToAsyncEnumerable(
        IEnumerable<ExcelRow> source,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
    {
        foreach (var row in source)
        {
            ct.ThrowIfCancellationRequested();
            yield return row;
        }
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
