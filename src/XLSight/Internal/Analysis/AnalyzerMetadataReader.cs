using System.Collections.Concurrent;
using System.Text;
using XLSight.Internal.Packaging;
using XLSight.Internal.Parsing;
using XLSight.Internal.Readers.Xlsx;
using XLSight.Analysis;

namespace XLSight.Internal.Analysis;

internal static class AnalyzerMetadataReader
{
    private const string TableRelationshipType = "http://schemas.openxmlformats.org/officeDocument/2006/relationships/table";
    private const string DrawingRelationshipType = "http://schemas.openxmlformats.org/officeDocument/2006/relationships/drawing";
    private const string PivotTableRelationshipType = "http://schemas.openxmlformats.org/officeDocument/2006/relationships/pivotTable";
    private const string CommentsRelationshipType = "http://schemas.openxmlformats.org/officeDocument/2006/relationships/comments";
    private const string ChartRelationshipType = "http://schemas.openxmlformats.org/officeDocument/2006/relationships/chart";
    private const string PivotCacheDefinitionRelationshipType = "http://schemas.openxmlformats.org/officeDocument/2006/relationships/pivotCacheDefinition";

    private static ReadOnlySpan<byte> TagTable => "table"u8;
    private static ReadOnlySpan<byte> TagTableColumn => "tableColumn"u8;
    private static ReadOnlySpan<byte> TagPivotTableDefinition => "pivotTableDefinition"u8;
    private static ReadOnlySpan<byte> TagLocation => "location"u8;
    private static ReadOnlySpan<byte> TagWorksheetSource => "worksheetSource"u8;
    private static ReadOnlySpan<byte> TagComment => "comment"u8;
    private static ReadOnlySpan<byte> TagFormula => "f"u8;

    private static ReadOnlySpan<byte> RefAttr => "ref="u8;
    private static ReadOnlySpan<byte> NameAttr => "name="u8;
    private static ReadOnlySpan<byte> DisplayNameAttr => "displayName="u8;
    private static ReadOnlySpan<byte> CacheIdAttr => "cacheId="u8;
    private static ReadOnlySpan<byte> SheetAttr => "sheet="u8;

    public static AnalyzerMetadata Read(XlsxPackage package, WorkbookMetadata metadata)
    {
        ArgumentNullException.ThrowIfNull(package);
        ArgumentNullException.ThrowIfNull(metadata);

        var allTables = new List<TableInfo>();
        var allPivotTables = new List<PivotTableInfo>();
        var allCharts = new List<ChartInfo>();
        var sheetsByPath = new Dictionary<string, SheetExactMetadata>(StringComparer.OrdinalIgnoreCase);

        foreach (var sheet in metadata.Sheets)
        {
            AddSheetMetadata(package, sheet, sheetsByPath, allTables, allPivotTables, allCharts);
        }

        return new AnalyzerMetadata
        {
            WorkbookExact = BuildWorkbookExact(metadata, allTables, allPivotTables, allCharts),
            SheetsByPath = sheetsByPath,
        };
    }

    private static List<TableInfo> ReadTables(XlsxPackage package, string sheetName, string[] tablePaths)
    {
        var tables = new List<TableInfo>(tablePaths.Length);
        foreach (string tablePath in tablePaths)
        {
            using var stream = TryOpenEntryBuffered(package, tablePath);
            if (stream is null)
            {
                continue;
            }

            using var buf = new ScanBuffer(stream);
            string? name = null;
            ExcelRange? range = null;
            var columns = new List<string>();

            while (true)
            {
                var span = buf.Span;
                if (!TryAdvanceTableTag(buf, span, ref name, ref range, columns))
                {
                    break;
                }
            }

            if (string.IsNullOrWhiteSpace(name) || range is null)
            {
                continue;
            }

            tables.Add(new TableInfo
            {
                Name = name,
                Sheet = sheetName,
                Range = range.Value,
                ColumnNames = columns,
            });
        }

        return tables;
    }

    private static List<PivotTableInfo> ReadPivots(XlsxPackage package, string sheetName, string[] pivotPaths)
    {
        var pivots = new List<PivotTableInfo>(pivotPaths.Length);
        foreach (string pivotPath in pivotPaths)
        {
            using var stream = TryOpenEntryBuffered(package, pivotPath);
            if (stream is null)
            {
                continue;
            }

            using var buf = new ScanBuffer(stream);
            string? name = null;
            string? cacheId = null;
            ExcelRange? range = null;

            while (true)
            {
                var span = buf.Span;
                if (!TryAdvancePivotTag(buf, span, ref name, ref cacheId, ref range))
                {
                    break;
                }
            }

            pivots.Add(new PivotTableInfo
            {
                Name = name ?? Path.GetFileNameWithoutExtension(pivotPath),
                Sheet = sheetName,
                Range = range,
                SourceReference = ReadPivotSource(package, pivotPath) ?? cacheId,
            });
        }

        return pivots;
    }

    private static string? ReadPivotSource(XlsxPackage package, string pivotPath)
    {
        IReadOnlyList<PackageRelationshipReader.RelationshipInfo> rels = ReadRelationships(package, pivotPath);
        var cacheRel = rels.FirstOrDefault(rel => string.Equals(rel.Type, PivotCacheDefinitionRelationshipType, StringComparison.Ordinal));
        if (cacheRel is null)
        {
            return null;
        }

        using var stream = TryOpenEntryBuffered(package, cacheRel.Target);
        if (stream is null)
        {
            return null;
        }

        using var buf = new ScanBuffer(stream);
        while (true)
        {
            var span = buf.Span;
            var status = XmlByteReader.TryFindStartTag(span, TagWorksheetSource, out var match, out int partialIndex);
            if (status == TagSearchResult.NotFound)
            {
                if (!XmlByteReader.RefillKeepingTagStart(buf, span, partialIndex))
                {
                    break;
                }

                continue;
            }

            if (status == TagSearchResult.NeedMoreData)
            {
                buf.Advance(match.Start);
                if (!buf.Refill())
                {
                    break;
                }

                continue;
            }

            var attrs = span.Slice(match.AfterName, match.EndExclusive - match.AfterName);
            string? sheet = TryGetUtf8Attribute(attrs, SheetAttr);
            string? reference = TryGetUtf8Attribute(attrs, RefAttr);
            if (!string.IsNullOrWhiteSpace(sheet) && !string.IsNullOrWhiteSpace(reference))
            {
                return $"{sheet}!{reference}";
            }

            buf.Advance(match.EndExclusive);
        }

        return null;
    }

    private static List<ChartInfo> ReadCharts(XlsxPackage package, string sheetName, IReadOnlyList<string> drawingPaths)
    {
        var charts = new List<ChartInfo>();
        foreach (string drawingPath in drawingPaths)
        {
            IReadOnlyList<PackageRelationshipReader.RelationshipInfo> rels = ReadRelationships(package, drawingPath);
            foreach (var rel in rels.Where(rel => string.Equals(rel.Type, ChartRelationshipType, StringComparison.Ordinal)))
            {
                ChartInfo? chart = ReadChart(package, sheetName, rel.Target);
                if (chart is not null)
                {
                    charts.Add(chart);
                }
            }
        }

        return charts;
    }

    private static ChartInfo? ReadChart(XlsxPackage package, string sheetName, string chartPath)
    {
        using var stream = TryOpenEntryBuffered(package, chartPath);
        if (stream is null)
        {
            return null;
        }

        using var buf = new ScanBuffer(stream);
        var refs = new List<string>();

        while (true)
        {
            var span = buf.Span;
            var status = XmlByteReader.TryFindStartTag(span, TagFormula, out var match, out int partialIndex);
            if (status == TagSearchResult.NotFound)
            {
                if (!XmlByteReader.RefillKeepingTagStart(buf, span, partialIndex))
                {
                    break;
                }

                continue;
            }

            if (status == TagSearchResult.NeedMoreData)
            {
                buf.Advance(match.Start);
                if (!buf.Refill())
                {
                    break;
                }

                continue;
            }

            buf.Advance(match.EndExclusive);
            if (!match.IsEmptyElement)
            {
                ReadOnlySpan<byte> valueBytes = XmlByteReader.ExtractUntilClose(buf, TagFormula);
                if (!valueBytes.IsEmpty)
                {
                    refs.Add(Encoding.UTF8.GetString(valueBytes));
                }
            }
        }

        return new ChartInfo
        {
            Title = null,
            Sheet = sheetName,
            PartPath = chartPath,
            SourceReferences = refs.Distinct(StringComparer.Ordinal).ToArray(),
        };
    }

    private static int ReadCommentCount(XlsxPackage package, string? commentsPath)
    {
        if (string.IsNullOrWhiteSpace(commentsPath))
        {
            return 0;
        }

        using var stream = TryOpenEntryBuffered(package, commentsPath);
        if (stream is null)
        {
            return 0;
        }

        int count = 0;
        using var buf = new ScanBuffer(stream);
        while (true)
        {
            var span = buf.Span;
            var status = XmlByteReader.TryFindStartTag(span, TagComment, out var match, out int partialIndex);
            if (status == TagSearchResult.NotFound)
            {
                if (!XmlByteReader.RefillKeepingTagStart(buf, span, partialIndex))
                {
                    break;
                }

                continue;
            }

            if (status == TagSearchResult.NeedMoreData)
            {
                buf.Advance(match.Start);
                if (!buf.Refill())
                {
                    break;
                }

                continue;
            }

            count++;
            buf.Advance(match.EndExclusive);
        }

        return count;
    }

    private static IReadOnlyList<PackageRelationshipReader.RelationshipInfo> ReadRelationships(XlsxPackage package, string ownerPath)
    {
        string relPath = BuildRelationshipsPath(ownerPath);
        using var relStream = TryOpenEntryBuffered(package, relPath);
        if (relStream is null)
        {
            return [];
        }

        return [.. PackageRelationshipReader.Read(relStream, ownerPath).Values];
    }

    private static Stream? TryOpenEntryBuffered(XlsxPackage package, string path)
    {
        Stream? fresh = package.TryOpenFreshEntry(path);
        if (fresh is not null)
        {
            return fresh;
        }

        return package.GetEntry(path)?.OpenBuffered();
    }

    private static string? TryGetUtf8Attribute(ReadOnlySpan<byte> attrBytes, ReadOnlySpan<byte> attrName)
        => CellAttributeParser.TryGetAttributeValue(attrBytes, attrName, out var valueBytes)
            ? Encoding.UTF8.GetString(valueBytes)
            : null;

    private static ExcelRange? TryParseRangeFromAttribute(ReadOnlySpan<byte> attrBytes)
    {
        if (!CellAttributeParser.TryGetAttributeValue(attrBytes, RefAttr, out var refBytes)) { return null; }
        Span<char> charBuf = stackalloc char[Math.Min(64, refBytes.Length)];
        if (refBytes.Length > charBuf.Length)
        {
            return AddressParser.TryParse(Encoding.UTF8.GetString(refBytes).AsSpan(), out ExcelRange range)
                ? range : null;
        }

        int chars = Encoding.UTF8.GetChars(refBytes, charBuf);
        return AddressParser.TryParse(charBuf[..chars], out ExcelRange parsed) ? parsed : null;
    }

    private static string BuildRelationshipsPath(string ownerPath)
    {
        int slash = ownerPath.LastIndexOf('/');
        string directory = slash >= 0 ? ownerPath[..slash] : string.Empty;
        string fileName = slash >= 0 ? ownerPath[(slash + 1)..] : ownerPath;
        return string.IsNullOrEmpty(directory)
            ? $"_rels/{fileName}.rels"
            : $"{directory}/_rels/{fileName}.rels";
    }

    private static WorkbookAnalysisExact BuildWorkbookExact(
        WorkbookMetadata metadata,
        List<TableInfo> allTables,
        List<PivotTableInfo> allPivotTables,
        List<ChartInfo> allCharts)
    {
        return new WorkbookAnalysisExact
        {
            NamedRanges = metadata.NamedRanges
                .Select(nr => new NamedRange
                {
                    Name = nr.Name,
                    Sheet = nr.ScopeSheetName,
                    Reference = nr.Reference,
                })
                .ToArray(),
            Tables = allTables,
            PivotTables = allPivotTables,
            Charts = allCharts,
            HasMacros = metadata.HasMacros,
            IsDate1904 = metadata.UsesDate1904,
            Warnings = [],
        };
    }

    private static void AddSheetMetadata(
        XlsxPackage package,
        WorkbookMetadata.WorkbookSheetInfo sheet,
        Dictionary<string, SheetExactMetadata> sheetsByPath,
        List<TableInfo> allTables,
        List<PivotTableInfo> allPivotTables,
        List<ChartInfo> allCharts)
    {
        var (meta, tables, pivots, charts) = GatherSheetSecondaryFiles(package, sheet);
        sheetsByPath[sheet.Path] = meta;
        allTables.AddRange(tables);
        allPivotTables.AddRange(pivots);
        allCharts.AddRange(charts);
    }

    private static (SheetExactMetadata Meta, List<TableInfo> Tables, List<PivotTableInfo> Pivots, List<ChartInfo> Charts)
        GatherSheetSecondaryFiles(XlsxPackage package, WorkbookMetadata.WorkbookSheetInfo sheet)
    {
        // Only reads secondary files; dimension/CF/DV/hyperlinks come from AnalysisSink.
        IReadOnlyList<PackageRelationshipReader.RelationshipInfo> rels = ReadRelationships(package, sheet.Path);

        string? commentsPath = rels.FirstOrDefault(rel => string.Equals(rel.Type, CommentsRelationshipType, StringComparison.Ordinal))?.Target;
        string[] drawingPaths = rels.Where(rel => string.Equals(rel.Type, DrawingRelationshipType, StringComparison.Ordinal))
                                    .Select(rel => rel.Target).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        string[] tablePaths = rels.Where(rel => string.Equals(rel.Type, TableRelationshipType, StringComparison.Ordinal))
                                  .Select(rel => rel.Target).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        string[] pivotPaths = rels.Where(rel => string.Equals(rel.Type, PivotTableRelationshipType, StringComparison.Ordinal))
                                  .Select(rel => rel.Target).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();

        List<TableInfo> tables = ReadTables(package, sheet.Name, tablePaths);
        List<PivotTableInfo> pivots = ReadPivots(package, sheet.Name, pivotPaths);
        List<ChartInfo> charts = ReadCharts(package, sheet.Name, drawingPaths);
        int commentCount = ReadCommentCount(package, commentsPath);

        return (new SheetExactMetadata
        {
            Exact = new SheetAnalysisExact
            {
                DeclaredDimension = null,
                MergedRegions = [],
                ConditionalFormattingCount = 0,
                DataValidationCount = 0,
                HyperlinkCount = 0,
                Tables = tables,
                PivotTables = pivots,
                Charts = charts,
                CommentCount = commentCount,
                DrawingCount = drawingPaths.Length,
            },
        }, tables, pivots, charts);
    }

    internal static AnalyzerMetadata ReadParallel(XlsxPackage package, WorkbookMetadata metadata)
    {
        ArgumentNullException.ThrowIfNull(package);
        ArgumentNullException.ThrowIfNull(metadata);

        if (metadata.Sheets.Count <= 1)
        {
            return Read(package, metadata);
        }

        var allTables = new ConcurrentBag<(int Order, TableInfo Info)>();
        var allPivotTables = new ConcurrentBag<(int Order, PivotTableInfo Info)>();
        var allCharts = new ConcurrentBag<(int Order, ChartInfo Info)>();
        var sheetsByPath = new ConcurrentDictionary<string, SheetExactMetadata>(StringComparer.OrdinalIgnoreCase);

        int dop = Math.Min(4, metadata.Sheets.Count);
        Parallel.ForEach(
            metadata.Sheets.Select((s, i) => (Sheet: s, Order: i)),
            new ParallelOptions { MaxDegreeOfParallelism = dop },
            item =>
            {
                var (meta, tables, pivots, charts) = GatherSheetSecondaryFiles(package, item.Sheet);
                sheetsByPath[item.Sheet.Path] = meta;
                foreach (var t in tables) { allTables.Add((item.Order, t)); }
                foreach (var p in pivots) { allPivotTables.Add((item.Order, p)); }
                foreach (var c in charts) { allCharts.Add((item.Order, c)); }
            });

        return new AnalyzerMetadata
        {
            WorkbookExact = BuildWorkbookExact(
                metadata,
                allTables.OrderBy(x => x.Order).Select(x => x.Info).ToList(),
                allPivotTables.OrderBy(x => x.Order).Select(x => x.Info).ToList(),
                allCharts.OrderBy(x => x.Order).Select(x => x.Info).ToList()),
            SheetsByPath = sheetsByPath,
        };
    }

    private static bool TryAdvanceTableTag(
        ScanBuffer buf,
        ReadOnlySpan<byte> span,
        ref string? name,
        ref ExcelRange? range,
        List<string> columns)
    {
        var tableStatus = XmlByteReader.TryFindStartTag(span, TagTable, out var tableMatch, out int tablePartial);
        var columnStatus = XmlByteReader.TryFindStartTag(span, TagTableColumn, out var columnMatch, out int columnPartial);

        bool tableFound = tableStatus == TagSearchResult.Found;
        bool columnFound = columnStatus == TagSearchResult.Found;
        if (!tableFound && !columnFound)
        {
            return XmlByteReader.RefillKeepingTagStart(buf, span, XmlByteReader.MaxPartial(tablePartial, columnPartial));
        }

        if ((tableStatus == TagSearchResult.NeedMoreData && !columnFound) ||
            (columnStatus == TagSearchResult.NeedMoreData && !tableFound))
        {
            int start = tableStatus == TagSearchResult.NeedMoreData ? tableMatch.Start : columnMatch.Start;
            buf.Advance(start);
            return buf.Refill();
        }

        if ((tableFound ? tableMatch.Start : int.MaxValue) < (columnFound ? columnMatch.Start : int.MaxValue))
        {
            var attrs = span.Slice(tableMatch.AfterName, tableMatch.EndExclusive - tableMatch.AfterName);
            name = TryGetUtf8Attribute(attrs, DisplayNameAttr) ?? TryGetUtf8Attribute(attrs, NameAttr);
            range = TryParseRangeFromAttribute(attrs);
            buf.Advance(tableMatch.EndExclusive);
            return true;
        }

        var columnAttrs = span.Slice(columnMatch.AfterName, columnMatch.EndExclusive - columnMatch.AfterName);
        string? columnName = TryGetUtf8Attribute(columnAttrs, NameAttr);
        if (!string.IsNullOrWhiteSpace(columnName))
        {
            columns.Add(columnName);
        }

        buf.Advance(columnMatch.EndExclusive);
        return true;
    }

    private static bool TryAdvancePivotTag(
        ScanBuffer buf,
        ReadOnlySpan<byte> span,
        ref string? name,
        ref string? cacheId,
        ref ExcelRange? range)
    {
        var pivotStatus = XmlByteReader.TryFindStartTag(span, TagPivotTableDefinition, out var pivotMatch, out int pivotPartial);
        var locationStatus = XmlByteReader.TryFindStartTag(span, TagLocation, out var locationMatch, out int locationPartial);

        bool pivotFound = pivotStatus == TagSearchResult.Found;
        bool locationFound = locationStatus == TagSearchResult.Found;
        if (!pivotFound && !locationFound)
        {
            return XmlByteReader.RefillKeepingTagStart(buf, span, XmlByteReader.MaxPartial(pivotPartial, locationPartial));
        }

        int pivotPos = pivotFound ? pivotMatch.Start : int.MaxValue;
        int locationPos = locationFound ? locationMatch.Start : int.MaxValue;
        if (pivotPos < locationPos)
        {
            var attrs = span.Slice(pivotMatch.AfterName, pivotMatch.EndExclusive - pivotMatch.AfterName);
            name = TryGetUtf8Attribute(attrs, NameAttr);
            cacheId = TryGetUtf8Attribute(attrs, CacheIdAttr);
            buf.Advance(pivotMatch.EndExclusive);
            return true;
        }

        var locationAttrs = span.Slice(locationMatch.AfterName, locationMatch.EndExclusive - locationMatch.AfterName);
        range = TryParseRangeFromAttribute(locationAttrs);
        buf.Advance(locationMatch.EndExclusive);
        return true;
    }

}
