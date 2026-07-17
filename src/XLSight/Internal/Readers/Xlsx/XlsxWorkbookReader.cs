using XLSight.Internal.Analysis;
using XLSight.Internal.Metadata;
using XLSight.Internal.Packaging;
using XLSight.Internal.Scanning;
using XLSight.Internal.Sinks;
using XLSight.Analysis;

namespace XLSight.Internal.Readers.Xlsx;

internal sealed class XlsxWorkbookReader : WorkbookReaderBase<WorkbookMetadata.WorkbookSheetInfo, SharedStringTable>
{
    private readonly WorkbookMetadata _metadata;
    private readonly Lazy<StyleTable> _styles;

    internal XlsxWorkbookReader(XlsxPackage package, WorkbookMetadata metadata, WorkbookFormat format = WorkbookFormat.Xlsx)
        : base(package, format, metadata.Sheets, metadata.UsesDate1904)
    {
        _metadata = metadata;
        _styles = new Lazy<StyleTable>(LoadStyles, LazyThreadSafetyMode.ExecutionAndPublication);
        Initialize();
    }

    protected override string GetSheetName(WorkbookMetadata.WorkbookSheetInfo sheet) => sheet.Name;

    protected override bool HasMacrosCore() => _metadata.HasMacros;

    protected override SharedStringTable LoadSharedStrings()
    {
        var entry = Package.GetEntry("xl/sharedStrings.xml");
        if (entry is null)
        {
            return SharedStringTable.Empty;
        }

        // Do NOT use 'using' — ownership is transferred to the lazy SharedStringTable,
        // which holds the stream open for on-demand pumping and disposes it when done.
        // Unbuffered: the parser reads through its own pooled ScanBuffer window.
        var stream = entry.Open();
        return SharedStringsParser.Parse(stream);
    }

    private StyleTable LoadStyles()
    {
        var entry = Package.GetEntry("xl/styles.xml");
        if (entry is null)
        {
            return StyleTable.Default;
        }

        using var stream = entry.OpenBuffered();
        return StylesParser.Parse(stream);
    }

    protected override AnalyzerMetadata BuildAnalyzerMetadata() => Package.IsFileBacked
        ? AnalyzerMetadataReader.ReadParallel(Package, _metadata)
        : AnalyzerMetadataReader.Read(Package, _metadata);

    protected override IRowCursor OpenCursorCore(
        WorkbookMetadata.WorkbookSheetInfo sheet,
        ExcelRange range,
        ReadMode mode,
        RowProjection? projection = null)
    {
        var sheetStream = OpenSheetStream(sheet.Path);
        try
        {
            var cursor = XlsxSheetScanner.OpenCursor(
                sheetStream,
                SharedStrings,
                _styles.Value,
                _metadata.UsesDate1904,
                mode,
                range,
                projection: projection);
            return new OwnedRowCursor(sheetStream, cursor);
        }
        catch
        {
            sheetStream.Dispose();
            throw;
        }
    }

    protected override SheetInfo AnalyzeSheetCore(
        WorkbookMetadata.WorkbookSheetInfo sheet,
        int sheetIndex,
        AnalyzerMetadata analysisMetadata,
        AnalysisLevel level,
        AnalysisOptions? options)
    {
        using var sheetStream = OpenSheetStream(sheet.Path);
        var sink = new AnalysisSink(SharedStrings, sheet.Name, level, options);
        XlsxSheetScanner.ScanSheet(sheetStream, SharedStrings, _styles.Value, _metadata.UsesDate1904, ReadMode.Values, ExcelRange.Unbounded, ref sink);
        return sink.Build(sheet.Name, sheetIndex, analysisMetadata.SheetsByPath[sheet.Path], level);
    }

    protected override void ScanWorksheetCore<TSink>(
        WorkbookMetadata.WorkbookSheetInfo sheet,
        ref TSink sink,
        CancellationToken ct)
    {
        using var sheetStream = OpenSheetStream(sheet.Path);
        var adapter = new WorksheetScanAdapter<TSink>(sink, ct);
        XlsxSheetScanner.ScanSheet(
            sheetStream,
            SharedStrings,
            _styles.Value,
            _metadata.UsesDate1904,
            ReadMode.Values,
            ExcelRange.Unbounded,
            ref adapter,
            includePostSheetMetadata: false);
        sink = adapter.Sink;
    }

    /// <summary>
    /// Opens a stream for a worksheet entry. Uses a fresh, independent ZipArchive when the
    /// package is file-backed (enabling concurrent calls); falls back to the shared archive
    /// for stream-backed workbooks.
    /// </summary>
    private Stream OpenSheetStream(string sheetPath)
    {
        // Every consumer wraps the stream in a ScanBuffer with its own pooled 64 KB window,
        // so an intermediate BufferedStream would only add a heap buffer and an extra copy.
        var freshStream = Package.TryOpenFreshEntryUnbuffered(sheetPath);
        if (freshStream is not null)
        {
            return freshStream;
        }

        var entry = Package.GetEntry(sheetPath)
            ?? throw new MalformedWorkbookException($"Worksheet entry '{sheetPath}' was not found in the package.");
        return entry.Open();
    }

    protected override RangeResult ReadRangeCore(string sheetName, ExcelRange range, ReadMode mode)
    {
        var (sheet, _) = FindSheet(sheetName);

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
        XlsxSheetScanner.ScanSheet(sheetStream, SharedStrings, _styles.Value, _metadata.UsesDate1904, mode, range, ref sink);

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

}
