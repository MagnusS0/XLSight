using XLSight.Analysis;
using XLSight.Internal.Analysis;
using XLSight.Internal.Metadata;
using XLSight.Internal.Packaging;
using XLSight.Internal.Scanning;
using XLSight.Internal.Sinks;
using XLSight.Internal.Vba;

namespace XLSight.Internal.Readers.Xlsb;

internal sealed class XlsbWorkbookReader : WorkbookReaderBase<XlsbSheetInfo, XlsbSharedStringTable>
{
    private const string TableRelationshipType = "http://schemas.openxmlformats.org/officeDocument/2006/relationships/table";
    private const string DrawingRelationshipType = "http://schemas.openxmlformats.org/officeDocument/2006/relationships/drawing";
    private const string CommentsRelationshipType = "http://schemas.openxmlformats.org/officeDocument/2006/relationships/comments";
    private const string PivotTableRelationshipType = "http://schemas.openxmlformats.org/officeDocument/2006/relationships/pivotTable";
    private const string PivotCacheDefinitionRelationshipType = "http://schemas.openxmlformats.org/officeDocument/2006/relationships/pivotCacheDefinition";

    private readonly XlsbMetadata _metadata;
    private readonly Lazy<StyleTable> _styles;

    internal XlsbWorkbookReader(XlsxPackage package, XlsbMetadata metadata)
        : base(package, WorkbookFormat.Xlsb, metadata.Sheets, metadata.UsesDate1904)
    {
        _metadata = metadata;
        _styles = new Lazy<StyleTable>(LoadStyles, LazyThreadSafetyMode.ExecutionAndPublication);
        Initialize();
    }

    protected override string GetSheetName(XlsbSheetInfo sheet) => sheet.Name;

    protected override bool HasMacrosCore() => Package.GetEntry("xl/vbaProject.bin") is not null;

    protected override IRowCursor OpenCursorCore(XlsbSheetInfo sheet, ExcelRange range, ReadMode mode, RowProjection? projection = null)
    {
        Stream sheetStream = OpenExclusiveSheetStream(sheet.Path);
        try
        {
            var cursor = new XlsbSheetCursor(
                sheetStream,
                SharedStringsLazy,
                _styles.Value,
                _metadata.UsesDate1904,
                mode,
                range,
                _metadata.FormulaContext,
                projection);
            return new OwnedRowCursor(sheetStream, cursor);
        }
        catch
        {
            sheetStream.Dispose();
            throw;
        }
    }

    protected override XlsbSharedStringTable LoadSharedStrings()
    {
        var entry = Package.GetEntry("xl/sharedStrings.bin");
        if (entry is null)
        {
            return XlsbSharedStringTable.Empty;
        }

        return XlsbSharedStringTable.Parse(entry.Open());
    }

    private StyleTable LoadStyles()
    {
        var entry = Package.GetEntry("xl/styles.bin");
        if (entry is null)
        {
            return StyleTable.Default;
        }

        using Stream stream = entry.Open();
        return XlsbStylesParser.Parse(stream);
    }

    protected override AnalyzerMetadata BuildAnalyzerMetadata()
    {
        var sheetsByPath = new Dictionary<string, SheetExactMetadata>(StringComparer.OrdinalIgnoreCase);
        var allTables = new List<TableInfo>();
        var allPivotTables = new List<PivotTableInfo>();
        var allCharts = new List<ChartInfo>();
        Dictionary<uint, string> workbookCachePaths = ReadWorkbookPivotCacheDefinitionPaths();
        foreach (XlsbSheetInfo sheet in _metadata.Sheets)
        {
            sheetsByPath[sheet.Path] = ReadSheetMetadata(
                sheet,
                workbookCachePaths,
                allTables,
                allPivotTables,
                allCharts);
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
                ExternalLinks = ExternalLinkMetadataReader.Read(Package, "xl/workbook.bin"),
                HasMacros = HasMacros,
                VbaProject = vbaProject,
                IsDate1904 = _metadata.UsesDate1904,
                Warnings = warnings,
            },
            SheetsByPath = sheetsByPath,
        };
    }

    private SheetExactMetadata ReadSheetMetadata(
        XlsbSheetInfo sheet,
        IReadOnlyDictionary<uint, string> workbookCachePaths,
        List<TableInfo> allTables,
        List<PivotTableInfo> allPivots,
        List<ChartInfo> allCharts)
    {
        var tables = new List<TableInfo>();
        var pivots = new List<PivotTableInfo>();
        var drawingPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        int commentCount = 0;
        foreach (PackageRelationshipReader.RelationshipInfo relationship in ReadRelationships(sheet.Path))
        {
            switch (relationship.Type)
            {
                case TableRelationshipType:
                    using (Stream? stream = Package.TryOpenEntryBuffered(relationship.Target))
                    {
                        TableInfo? table = stream is null ? null : XlsbTableParser.Parse(stream, sheet.Name);
                        if (table is not null) { tables.Add(table); }
                    }
                    break;

                case PivotTableRelationshipType:
                    PivotTableInfo? pivot = ReadPivot(sheet.Name, relationship.Target, workbookCachePaths);
                    if (pivot is not null) { pivots.Add(pivot); }
                    break;

                case DrawingRelationshipType:
                    drawingPaths.Add(relationship.Target);
                    break;

                case CommentsRelationshipType:
                    using (Stream? stream = Package.TryOpenEntryBuffered(relationship.Target))
                    {
                        if (stream is not null) { commentCount += XlsbCommentsParser.Count(stream); }
                    }
                    break;
            }
        }

        List<ChartInfo> charts = ChartMetadataReader.ReadCharts(Package, sheet.Name, [.. drawingPaths]);
        allTables.AddRange(tables);
        allPivots.AddRange(pivots);
        allCharts.AddRange(charts);
        return new SheetExactMetadata
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
                DataValidations = [],
                HyperlinkCount = 0,
                CommentCount = commentCount,
                DrawingCount = drawingPaths.Count,
            },
        };
    }

    private PivotTableInfo? ReadPivot(
        string sheetName,
        string pivotPath,
        IReadOnlyDictionary<uint, string> workbookCachePaths)
    {
        try
        {
            using Stream? stream = Package.TryOpenEntryBuffered(pivotPath);
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
            using Stream? stream = Package.TryOpenEntryBuffered(cachePath);
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
            using Stream? stream = Package.TryOpenEntryBuffered("xl/workbook.bin");
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
        using Stream? stream = Package.TryOpenEntryBuffered(relationshipPath);
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

    protected override SheetInfo AnalyzeSheetCore(
        XlsbSheetInfo sheet,
        int sheetIndex,
        AnalyzerMetadata analysisMetadata,
        AnalysisLevel level,
        AnalysisOptions? options)
    {
        using Stream sheetStream = OpenConcurrentSheetStream(sheet.Path);
        var sink = new AnalysisSink(SharedStrings, sheet.Name, level, options);
        XlsbWorksheetScanner.ScanSheet(
            sheetStream,
            SharedStringsLazy,
            _styles.Value,
            _metadata.UsesDate1904,
            ReadMode.Values,
            ExcelRange.Unbounded,
            ref sink,
            _metadata.FormulaContext);
        return sink.Build(sheet.Name, sheetIndex, analysisMetadata.SheetsByPath[sheet.Path], level);
    }

    protected override void ScanWorksheetCore<TSink>(XlsbSheetInfo sheet, ref TSink sink)
    {
        using Stream sheetStream = OpenConcurrentSheetStream(sheet.Path);
        var adapter = new WorksheetScanAdapter<TSink>(sink);
        XlsbWorksheetScanner.ScanSheet(
            sheetStream,
            SharedStringsLazy,
            _styles.Value,
            _metadata.UsesDate1904,
            ReadMode.Values,
            ExcelRange.Unbounded,
            ref adapter,
            _metadata.FormulaContext,
            includePostSheetMetadata: false);
        sink = adapter.Sink;
    }

    private Stream OpenConcurrentSheetStream(string sheetPath)
    {
        Stream? freshStream = Package.TryOpenFreshEntryUnbuffered(sheetPath);
        if (freshStream is not null)
        {
            return freshStream;
        }

        var entry = Package.GetEntry(sheetPath)
            ?? throw new MalformedWorkbookException($"Worksheet entry '{sheetPath}' was not found in the package.");
        return entry.Open();
    }

    private Stream OpenExclusiveSheetStream(string sheetPath)
    {
        var entry = Package.GetEntry(sheetPath)
            ?? throw new MalformedWorkbookException($"Worksheet entry '{sheetPath}' was not found in the package.");
        return entry.Open();
    }

}
