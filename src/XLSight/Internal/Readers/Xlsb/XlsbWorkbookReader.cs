using XLSight.Analysis;
using XLSight.Internal.Analysis;
using XLSight.Internal.Metadata;
using XLSight.Internal.Packaging;
using XLSight.Internal.Sinks;
using XLSight.Internal.Vba;

namespace XLSight.Internal.Readers.Xlsb;

internal sealed class XlsbWorkbookReader : IWorkbookReader
{
    private const string TableRelationshipType = "http://schemas.openxmlformats.org/officeDocument/2006/relationships/table";
    private const string DrawingRelationshipType = "http://schemas.openxmlformats.org/officeDocument/2006/relationships/drawing";
    private const string CommentsRelationshipType = "http://schemas.openxmlformats.org/officeDocument/2006/relationships/comments";
    private const string PivotTableRelationshipType = "http://schemas.openxmlformats.org/officeDocument/2006/relationships/pivotTable";
    private const string PivotCacheDefinitionRelationshipType = "http://schemas.openxmlformats.org/officeDocument/2006/relationships/pivotCacheDefinition";

    private readonly XlsxPackage _package;
    private readonly XlsbMetadata _metadata;
    private readonly Lazy<XlsbSharedStringTable> _sharedStrings;
    private readonly Lazy<StyleTable> _styles;
    private readonly Lazy<AnalyzerMetadata> _analyzerMetadata;
    private readonly Lazy<string[]> _sheetNames;
    private volatile bool _disposed;

    internal XlsbWorkbookReader(XlsxPackage package, XlsbMetadata metadata)
    {
        _package = package;
        _metadata = metadata;
        _sharedStrings = new Lazy<XlsbSharedStringTable>(LoadSharedStrings, LazyThreadSafetyMode.ExecutionAndPublication);
        _styles = new Lazy<StyleTable>(LoadStyles, LazyThreadSafetyMode.ExecutionAndPublication);
        _analyzerMetadata = new Lazy<AnalyzerMetadata>(BuildAnalyzerMetadata, LazyThreadSafetyMode.ExecutionAndPublication);
        _sheetNames = new Lazy<string[]>(() => _metadata.Sheets.Select(sheet => sheet.Name).ToArray(), LazyThreadSafetyMode.PublicationOnly);
    }

    public bool IsFileBacked => _package.IsFileBacked;

    public WorkbookFormat Format
    {
        get
        {
            ThrowIfDisposed();
            return WorkbookFormat.Xlsb;
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
            return _package.GetEntry("xl/vbaProject.bin") is not null;
        }
    }

    public VbaProjectInfo? GetVbaProject()
    {
        ThrowIfDisposed();
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
        ThrowIfDisposed();
        var range = new ExcelRange(address, address);
        return ReadRange(sheetName, range, mode).Cells.Span[0];
    }

    public RangeResult ReadRange(string sheetName, ExcelRange range, ReadMode mode)
    {
        ThrowIfDisposed();
        return ReadRangeCore(sheetName, range, mode);
    }

    public Task<ExcelCellValue> ReadCellAsync(string sheetName, ExcelAddress address, ReadMode mode, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var range = new ExcelRange(address, address);
        RangeResult result = ReadRange(sheetName, range, mode);
        return Task.FromResult(result.Cells.Span[0]);
    }

    public Task<RangeResult> ReadRangeAsync(string sheetName, ExcelRange range, ReadMode mode, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        ThrowIfDisposed();
        return Task.FromResult(ReadRangeCore(sheetName, range, mode));
    }

    public IRowCursor OpenCursor(string sheetName, ExcelRange range, ReadMode mode)
    {
        ThrowIfDisposed();

        XlsbSheetInfo sheet = FindSheet(sheetName);
        Stream sheetStream = OpenSheetStream(sheet.Path);
        try
        {
            var cursor = XlsbWorksheetScanner.OpenCursor(
                sheetStream,
                _sharedStrings,
                _styles.Value,
                _metadata.UsesDate1904,
                mode,
                range);
            return new OwnedRowCursor(sheetStream, cursor);
        }
        catch
        {
            sheetStream.Dispose();
            throw;
        }
    }

    public WorkbookInfo Analyze(AnalysisLevel level, int maxDegreeOfParallelism = -1)
    {
        ThrowIfDisposed();

        AnalyzerMetadata analysisMetadata = _analyzerMetadata.Value;
        int dop = ResolveSheetDop(maxDegreeOfParallelism);
        var sheets = _package.IsFileBacked && dop > 1
            ? AnalyzeSheetsParallel(analysisMetadata, level, dop)
            : _metadata.Sheets.Select((sheet, i) => AnalyzeSheetCore(sheet, i, analysisMetadata, level)).ToList();

        return new WorkbookInfo
        {
            Level = level,
            Sheets = sheets,
            Exact = analysisMetadata.WorkbookExact,
            AnalyzedAtUtc = DateTimeOffset.UtcNow,
        };
    }

    public SheetInfo AnalyzeSheet(string sheetName, AnalysisLevel level)
    {
        ThrowIfDisposed();

        var (sheet, sheetIndex) = FindSheetWithIndex(sheetName);
        return AnalyzeSheetCore(sheet, sheetIndex, _analyzerMetadata.Value, level);
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
            sheets = [];
            foreach (var (sheet, i) in _metadata.Sheets.Select((sheet, i) => (sheet, i)))
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

    public Task<SheetInfo> AnalyzeSheetAsync(string sheetName, AnalysisLevel level, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        ThrowIfDisposed();

        var (sheet, sheetIndex) = FindSheetWithIndex(sheetName);
        AnalyzerMetadata analysisMetadata = _analyzerMetadata.Value;
        return Task.Run(() => AnalyzeSheetCore(sheet, sheetIndex, analysisMetadata, level), ct);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        if (_sharedStrings.IsValueCreated)
        {
            _sharedStrings.Value.Dispose();
        }

        _package.Dispose();
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        if (_sharedStrings.IsValueCreated)
        {
            _sharedStrings.Value.Dispose();
        }

        await _package.DisposeAsync().ConfigureAwait(false);
    }

    private XlsbSharedStringTable LoadSharedStrings()
    {
        var entry = _package.GetEntry("xl/sharedStrings.bin");
        if (entry is null)
        {
            return XlsbSharedStringTable.Empty;
        }

        Stream stream = entry.Open();
        return XlsbSharedStringsParser.Parse(stream);
    }

    private StyleTable LoadStyles()
    {
        var entry = _package.GetEntry("xl/styles.bin");
        if (entry is null)
        {
            return StyleTable.Default;
        }

        using Stream stream = entry.OpenBuffered();
        return XlsbStylesParser.Parse(stream);
    }

    private AnalyzerMetadata BuildAnalyzerMetadata()
    {
        var sheetsByPath = new Dictionary<string, SheetExactMetadata>(StringComparer.OrdinalIgnoreCase);
        var allTables = new List<TableInfo>();
        var allPivotTables = new List<PivotTableInfo>();
        var allCharts = new List<ChartInfo>();
        Dictionary<uint, string> workbookCachePaths = ReadWorkbookPivotCacheDefinitionPaths();
        foreach (XlsbSheetInfo sheet in _metadata.Sheets)
        {
            IReadOnlyList<PackageRelationshipReader.RelationshipInfo> relationships = ReadRelationships(sheet.Path);
            List<TableInfo> tables = ReadSheetTables(sheet, relationships);
            List<PivotTableInfo> pivots = ReadSheetPivots(sheet, relationships, workbookCachePaths);
            var (charts, drawingCount) = ReadSheetCharts(sheet, relationships);
            int commentCount = ReadSheetCommentCount(relationships);
            sheetsByPath[sheet.Path] = CreateSheetMetadata(tables, pivots, charts, commentCount, drawingCount);
            allTables.AddRange(tables);
            allPivotTables.AddRange(pivots);
            allCharts.AddRange(charts);
        }

        var warnings = new List<AnalysisWarning>();
        VbaProjectInfo? vbaProject = TryReadVbaProject(warnings);

        return new AnalyzerMetadata
        {
            WorkbookExact = new WorkbookAnalysisExact
            {
                NamedRanges = _metadata.DefinedNames.Select(static name => new NamedRange
                {
                    Name = name.Name,
                    Sheet = name.ScopeSheetName,
                    Reference = name.Reference,
                }).ToArray(),
                Tables = allTables,
                PivotTables = allPivotTables,
                Charts = allCharts,
                HasMacros = HasMacros,
                VbaProject = vbaProject,
                IsDate1904 = _metadata.UsesDate1904,
                Warnings = warnings,
            },
            SheetsByPath = sheetsByPath,
        };
    }

    private List<TableInfo> ReadSheetTables(
        XlsbSheetInfo sheet,
        IReadOnlyList<PackageRelationshipReader.RelationshipInfo> relationships)
    {
        var tables = new List<TableInfo>();
        foreach (PackageRelationshipReader.RelationshipInfo relationship in relationships)
        {
            if (!string.Equals(relationship.Type, TableRelationshipType, StringComparison.Ordinal))
            {
                continue;
            }

            using Stream? stream = _package.TryOpenEntryBuffered(relationship.Target);
            if (stream is null)
            {
                continue;
            }

            TableInfo? table = XlsbTableParser.Parse(stream, sheet.Name);
            if (table is not null)
            {
                tables.Add(table);
            }
        }

        return tables;
    }

    private (List<ChartInfo> Charts, int DrawingCount) ReadSheetCharts(
        XlsbSheetInfo sheet,
        IReadOnlyList<PackageRelationshipReader.RelationshipInfo> relationships)
    {
        string[] drawingPaths = relationships
            .Where(rel => string.Equals(rel.Type, DrawingRelationshipType, StringComparison.Ordinal))
            .Select(rel => rel.Target)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return (ChartMetadataReader.ReadCharts(_package, sheet.Name, drawingPaths), drawingPaths.Length);
    }

    private int ReadSheetCommentCount(IReadOnlyList<PackageRelationshipReader.RelationshipInfo> relationships)
    {
        int count = 0;
        foreach (PackageRelationshipReader.RelationshipInfo relationship in relationships)
        {
            if (!string.Equals(relationship.Type, CommentsRelationshipType, StringComparison.Ordinal))
            {
                continue;
            }

            using Stream? stream = _package.TryOpenEntryBuffered(relationship.Target);
            if (stream is not null)
            {
                count += XlsbCommentsParser.Count(stream);
            }
        }

        return count;
    }

    private List<PivotTableInfo> ReadSheetPivots(
        XlsbSheetInfo sheet,
        IReadOnlyList<PackageRelationshipReader.RelationshipInfo> relationships,
        IReadOnlyDictionary<uint, string> workbookCachePaths)
    {
        var pivots = new List<PivotTableInfo>();
        foreach (PackageRelationshipReader.RelationshipInfo relationship in relationships)
        {
            if (!string.Equals(relationship.Type, PivotTableRelationshipType, StringComparison.Ordinal))
            {
                continue;
            }

            PivotTableInfo? pivot = ReadPivot(sheet.Name, relationship.Target, workbookCachePaths);
            if (pivot is not null)
            {
                pivots.Add(pivot);
            }
        }

        return pivots;
    }

    private PivotTableInfo? ReadPivot(
        string sheetName,
        string pivotPath,
        IReadOnlyDictionary<uint, string> workbookCachePaths)
    {
        try
        {
            using Stream? stream = _package.TryOpenEntryBuffered(pivotPath);
            if (stream is null)
            {
                return null;
            }

            XlsbPivotParser.PivotTableMetadata metadata = XlsbPivotParser.ParsePivotTable(stream);
            return new PivotTableInfo
            {
                Name = metadata.Name ?? Path.GetFileNameWithoutExtension(pivotPath),
                Sheet = sheetName,
                Range = metadata.Range,
                SourceReference = ReadPivotSource(pivotPath, metadata.CacheId, workbookCachePaths)
                    ?? metadata.CacheId?.ToString(System.Globalization.CultureInfo.InvariantCulture),
            };
        }
        catch (Exception ex) when (ex is MalformedWorkbookException or IOException or InvalidDataException or ArgumentException or ArithmeticException)
        {
            return null;
        }
    }

    private string? ReadPivotSource(
        string pivotPath,
        uint? cacheId,
        IReadOnlyDictionary<uint, string> workbookCachePaths)
    {
        string? cachePath = ReadPivotCacheDefinitionPathFromPivotRelationships(pivotPath);
        if (cachePath is null && cacheId is not null)
        {
            workbookCachePaths.TryGetValue(cacheId.Value, out cachePath);
        }

        if (cachePath is null)
        {
            return null;
        }

        try
        {
            using Stream? stream = _package.TryOpenEntryBuffered(cachePath);
            return stream is null ? null : XlsbPivotParser.ParsePivotCacheSource(stream);
        }
        catch (Exception ex) when (ex is MalformedWorkbookException or IOException or InvalidDataException or ArgumentException or ArithmeticException)
        {
            return null;
        }
    }

    private string? ReadPivotCacheDefinitionPathFromPivotRelationships(string pivotPath)
    {
        IReadOnlyList<PackageRelationshipReader.RelationshipInfo> relationships = ReadRelationships(pivotPath);
        return relationships
            .FirstOrDefault(rel => string.Equals(rel.Type, PivotCacheDefinitionRelationshipType, StringComparison.Ordinal))
            ?.Target;
    }

    private Dictionary<uint, string> ReadWorkbookPivotCacheDefinitionPaths()
    {
        try
        {
            using Stream? stream = _package.TryOpenEntryBuffered("xl/workbook.bin");
            if (stream is null)
            {
                return new Dictionary<uint, string>();
            }

            Dictionary<uint, string> relIdsByCacheId = XlsbPivotParser.ParseWorkbookPivotCacheRelationships(stream);
            if (relIdsByCacheId.Count == 0)
            {
                return relIdsByCacheId;
            }

            IReadOnlyList<PackageRelationshipReader.RelationshipInfo> relationships = ReadRelationships("xl/workbook.bin");
            var pathsByCacheId = new Dictionary<uint, string>();
            foreach ((uint cacheId, string relationshipId) in relIdsByCacheId)
            {
                PackageRelationshipReader.RelationshipInfo? relationship = relationships.FirstOrDefault(
                    rel => string.Equals(rel.Id, relationshipId, StringComparison.Ordinal) &&
                        string.Equals(rel.Type, PivotCacheDefinitionRelationshipType, StringComparison.Ordinal));
                if (relationship is not null)
                {
                    pathsByCacheId[cacheId] = relationship.Target;
                }
            }

            return pathsByCacheId;
        }
        catch (Exception ex) when (ex is MalformedWorkbookException or IOException or InvalidDataException or ArgumentException or ArithmeticException)
        {
            return new Dictionary<uint, string>();
        }
    }

    private IReadOnlyList<PackageRelationshipReader.RelationshipInfo> ReadRelationships(string ownerPath)
    {
        string relationshipPath = XlsxPackage.BuildRelationshipsPath(ownerPath);
        using Stream? stream = _package.TryOpenEntryBuffered(relationshipPath);
        if (stream is null)
        {
            return [];
        }

        return [.. PackageRelationshipReader.Read(stream, ownerPath).Values];
    }

    private VbaProjectInfo? TryReadVbaProject(List<AnalysisWarning> warnings)
    {
        if (!HasMacros)
        {
            return null;
        }

        try
        {
            return GetVbaProject();
        }
        catch (Exception ex) when (ex is VbaProjectParseException or IOException or InvalidDataException or ArgumentException or ArithmeticException)
        {
            warnings.Add(new AnalysisWarning
            {
                Code = "vba.parse.failed",
                Message = $"VBA project metadata could not be parsed: {ex.Message}",
            });
            return null;
        }
    }

    private List<SheetInfo> AnalyzeSheetsParallel(AnalyzerMetadata analysisMetadata, AnalysisLevel level, int dop)
    {
        var results = new SheetInfo[_metadata.Sheets.Count];
        Parallel.For(
            0,
            _metadata.Sheets.Count,
            new ParallelOptions { MaxDegreeOfParallelism = dop },
            i => results[i] = AnalyzeSheetCore(_metadata.Sheets[i], i, analysisMetadata, level));
        return [.. results];
    }

    private async Task<List<SheetInfo>> AnalyzeSheetsParallelAsync(
        AnalyzerMetadata analysisMetadata,
        AnalysisLevel level,
        int dop,
        CancellationToken ct)
    {
        var results = new SheetInfo[_metadata.Sheets.Count];
        await Parallel.ForEachAsync(
            Enumerable.Range(0, _metadata.Sheets.Count),
            new ParallelOptions { MaxDegreeOfParallelism = dop, CancellationToken = ct },
            async (i, ct2) =>
            {
                results[i] = await Task.Run(
                    () => AnalyzeSheetCore(_metadata.Sheets[i], i, analysisMetadata, level),
                    ct2).ConfigureAwait(false);
            }).ConfigureAwait(false);
        return [.. results];
    }

    private int ResolveSheetDop(int requested)
    {
        int count = _metadata.Sheets.Count;
        if (requested == 1 || count <= 1)
        {
            return 1;
        }

        if (requested <= 0)
        {
            return Math.Min(Environment.ProcessorCount, count);
        }

        return Math.Min(requested, count);
    }

    private SheetInfo AnalyzeSheetCore(
        XlsbSheetInfo sheet,
        int sheetIndex,
        AnalyzerMetadata analysisMetadata,
        AnalysisLevel level)
    {
        using Stream sheetStream = OpenSheetStream(sheet.Path);
        var sink = new AnalysisSink(_sharedStrings.Value, level);
        XlsbWorksheetScanner.ScanSheet(
            sheetStream,
            _sharedStrings,
            _styles.Value,
            _metadata.UsesDate1904,
            ReadMode.Values,
            ExcelRange.Unbounded,
            ref sink);
        return sink.Build(sheet.Name, sheetIndex, analysisMetadata.SheetsByPath[sheet.Path], level);
    }

    private static SheetExactMetadata CreateSheetMetadata(
        IReadOnlyList<TableInfo> tables,
        IReadOnlyList<PivotTableInfo> pivots,
        IReadOnlyList<ChartInfo> charts,
        int commentCount,
        int drawingCount)
        => new()
        {
            Exact = new SheetAnalysisExact
            {
                DeclaredDimension = null,
                MergedRegions = [],
                Tables = tables,
                PivotTables = pivots,
                Charts = charts,
                ConditionalFormattingCount = 0,
                DataValidationCount = 0,
                HyperlinkCount = 0,
                CommentCount = commentCount,
                DrawingCount = drawingCount,
            },
        };

    private RangeResult ReadRangeCore(string sheetName, ExcelRange range, ReadMode mode)
    {
        if (range.IsUnbounded)
        {
            throw new RangeTooLargeException(0, ExcelLimits.MaxCells);
        }

        long cellCount = (long)range.Width * range.Height;
        if (cellCount > ExcelLimits.MaxCells)
        {
            throw new RangeTooLargeException(cellCount, ExcelLimits.MaxCells);
        }

        XlsbSheetInfo sheet = FindSheet(sheetName);
        using Stream sheetStream = OpenSheetStream(sheet.Path);
        var buffer = new ExcelCellValue[cellCount];

        foreach (ExcelRow row in XlsbWorksheetScanner.ScanRows(
            sheetStream,
            _sharedStrings,
            _styles.Value,
            _metadata.UsesDate1904,
            mode,
            range))
        {
            int rowOffset = (row.RowIndex - range.TopLeft.Row) * range.Width;
            for (int column = range.TopLeft.Column; column <= range.BottomRight.Column; column++)
            {
                buffer[rowOffset + (column - range.TopLeft.Column)] = row.GetCell(column);
            }
        }

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

    private Stream OpenSheetStream(string sheetPath)
    {
        Stream? freshStream = _package.TryOpenFreshEntry(sheetPath);
        if (freshStream is not null)
        {
            return freshStream;
        }

        var entry = _package.GetEntry(sheetPath)
            ?? throw new MalformedWorkbookException($"Worksheet entry '{sheetPath}' was not found in the package.");
        return entry.Open();
    }

    private Stream? OpenVbaProjectStream() => _package.TryOpenEntryBuffered("xl/vbaProject.bin");

    private Stream OpenRequiredVbaProjectStream()
        => OpenVbaProjectStream()
            ?? throw new InvalidOperationException("The workbook does not contain a VBA macro project.");

    private XlsbSheetInfo FindSheet(string sheetName)
    {
        foreach (XlsbSheetInfo sheet in _metadata.Sheets)
        {
            if (string.Equals(sheet.Name, sheetName, StringComparison.OrdinalIgnoreCase))
            {
                return sheet;
            }
        }

        throw new SheetNotFoundException(sheetName);
    }

    private (XlsbSheetInfo Sheet, int Index) FindSheetWithIndex(string sheetName)
    {
        for (int i = 0; i < _metadata.Sheets.Count; i++)
        {
            XlsbSheetInfo sheet = _metadata.Sheets[i];
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
            ThrowHelpers.ThrowObjectDisposed(nameof(XlsbWorkbookReader));
        }
    }
}
