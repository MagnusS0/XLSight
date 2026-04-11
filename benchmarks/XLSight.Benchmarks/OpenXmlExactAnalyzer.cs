using System.IO.Compression;
using System.Xml.Linq;

public static class OpenXmlExactAnalyzer
{
    private const string SpreadsheetNamespace = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
    private const string WorkbookRelationshipNamespace = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
    private const string PackageRelationshipNamespace = "http://schemas.openxmlformats.org/package/2006/relationships";
    private const string TableRelationshipType = "http://schemas.openxmlformats.org/officeDocument/2006/relationships/table";
    private const string DrawingRelationshipType = "http://schemas.openxmlformats.org/officeDocument/2006/relationships/drawing";
    private const string PivotTableRelationshipType = "http://schemas.openxmlformats.org/officeDocument/2006/relationships/pivotTable";
    private const string CommentsRelationshipType = "http://schemas.openxmlformats.org/officeDocument/2006/relationships/comments";
    private const string ChartRelationshipType = "http://schemas.openxmlformats.org/officeDocument/2006/relationships/chart";
    private const string PivotCacheDefinitionRelationshipType = "http://schemas.openxmlformats.org/officeDocument/2006/relationships/pivotCacheDefinition";

    private static readonly XNamespace s_mainNs = SpreadsheetNamespace;
    private static readonly XNamespace s_relNs = WorkbookRelationshipNamespace;
    private static readonly XNamespace s_pkgRelNs = PackageRelationshipNamespace;
    private static readonly XNamespace s_chartNs = "http://schemas.openxmlformats.org/drawingml/2006/chart";

    public static int AnalyzeWorkbook(string path)
    {
        using var stream = File.OpenRead(path);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: false);

        XDocument workbook = LoadXml(archive, "xl/workbook.xml");
        var workbookRelationships = ReadRelationships(archive, "xl/workbook.xml");

        int fingerprint = 0;
        XElement? workbookRoot = workbook.Root;
        if (workbookRoot is null)
        {
            return 0;
        }

        fingerprint += workbookRoot.Element(s_mainNs + "definedNames")?.Elements(s_mainNs + "definedName")
            .Sum(definedName => ((string?)definedName.Attribute("name"))?.Length ?? 0)
            ?? 0;

        if ((bool?)workbookRoot.Element(s_mainNs + "workbookPr")?.Attribute("date1904") == true)
        {
            fingerprint++;
        }

        if (TryGetEntry(archive, "xl/vbaProject.bin") is not null)
        {
            fingerprint++;
        }

        foreach (XElement sheet in workbookRoot.Element(s_mainNs + "sheets")?.Elements(s_mainNs + "sheet") ?? [])
        {
            string sheetName = (string?)sheet.Attribute("name") ?? string.Empty;
            string relationshipId = (string?)sheet.Attribute(s_relNs + "id") ?? string.Empty;
            if (!workbookRelationships.TryGetValue(relationshipId, out RelationshipInfo? sheetRelationship))
            {
                continue;
            }

            fingerprint += sheetName.Length;
            fingerprint += AnalyzeSheet(archive, sheetRelationship.Target);
        }

        return fingerprint;
    }

    private static int AnalyzeSheet(ZipArchive archive, string sheetPath)
    {
        XDocument worksheet = LoadXml(archive, sheetPath);
        var relationships = ReadRelationships(archive, sheetPath);

        int fingerprint = 0;
        XElement? root = worksheet.Root;
        if (root is null)
        {
            return 0;
        }

        fingerprint += ((string?)root.Element(s_mainNs + "dimension")?.Attribute("ref"))?.Length ?? 0;
        fingerprint += root.Descendants(s_mainNs + "mergeCell").Count();
        fingerprint += root.Descendants(s_mainNs + "conditionalFormatting").Count();
        fingerprint += root.Descendants(s_mainNs + "dataValidation").Count();
        fingerprint += root.Descendants(s_mainNs + "hyperlink").Count();

        List<string> drawingPaths = [];
        List<string> tablePaths = [];
        List<string> pivotPaths = [];
        string? commentsPath = null;

        foreach (RelationshipInfo relationship in relationships.Values)
        {
            if (string.Equals(relationship.Type, DrawingRelationshipType, StringComparison.Ordinal))
            {
                drawingPaths.Add(relationship.Target);
            }
            else if (string.Equals(relationship.Type, TableRelationshipType, StringComparison.Ordinal))
            {
                tablePaths.Add(relationship.Target);
            }
            else if (string.Equals(relationship.Type, PivotTableRelationshipType, StringComparison.Ordinal))
            {
                pivotPaths.Add(relationship.Target);
            }
            else if (string.Equals(relationship.Type, CommentsRelationshipType, StringComparison.Ordinal))
            {
                commentsPath = relationship.Target;
            }
        }

        fingerprint += drawingPaths.Count;
        fingerprint += tablePaths.Sum(path => AnalyzeTable(archive, path));
        fingerprint += pivotPaths.Sum(path => AnalyzePivot(archive, path));
        fingerprint += drawingPaths.Sum(path => AnalyzeDrawingCharts(archive, path));
        fingerprint += AnalyzeComments(archive, commentsPath);

        return fingerprint;
    }

    private static int AnalyzeTable(ZipArchive archive, string tablePath)
    {
        XDocument table = LoadXml(archive, tablePath);
        XElement? root = table.Root;
        if (root is null)
        {
            return 0;
        }

        int fingerprint = 0;
        fingerprint += ((string?)root.Attribute("displayName"))?.Length ?? 0;
        fingerprint += ((string?)root.Attribute("name"))?.Length ?? 0;
        fingerprint += ((string?)root.Attribute("ref"))?.Length ?? 0;
        fingerprint += root.Descendants(s_mainNs + "tableColumn")
            .Sum(column => ((string?)column.Attribute("name"))?.Length ?? 0);
        return fingerprint;
    }

    private static int AnalyzePivot(ZipArchive archive, string pivotPath)
    {
        XDocument pivot = LoadXml(archive, pivotPath);
        XElement? root = pivot.Root;
        if (root is null)
        {
            return 0;
        }

        int fingerprint = 0;
        fingerprint += ((string?)root.Attribute("name"))?.Length ?? 0;
        fingerprint += ((string?)root.Attribute("cacheId"))?.Length ?? 0;
        fingerprint += ((string?)root.Descendants(s_mainNs + "location").FirstOrDefault()?.Attribute("ref"))?.Length ?? 0;

        var relationships = ReadRelationships(archive, pivotPath);
        foreach (RelationshipInfo relationship in relationships.Values)
        {
            if (!string.Equals(relationship.Type, PivotCacheDefinitionRelationshipType, StringComparison.Ordinal))
            {
                continue;
            }

            fingerprint += AnalyzePivotCacheDefinition(archive, relationship.Target);
        }

        return fingerprint;
    }

    private static int AnalyzePivotCacheDefinition(ZipArchive archive, string cacheDefinitionPath)
    {
        XDocument cacheDefinition = LoadXml(archive, cacheDefinitionPath);
        XElement? worksheetSource = cacheDefinition.Root?.Descendants(s_mainNs + "worksheetSource").FirstOrDefault();
        if (worksheetSource is null)
        {
            return 0;
        }

        int fingerprint = 0;
        fingerprint += ((string?)worksheetSource.Attribute("sheet"))?.Length ?? 0;
        fingerprint += ((string?)worksheetSource.Attribute("ref"))?.Length ?? 0;
        return fingerprint;
    }

    private static int AnalyzeDrawingCharts(ZipArchive archive, string drawingPath)
    {
        var relationships = ReadRelationships(archive, drawingPath);
        int fingerprint = 0;

        foreach (RelationshipInfo relationship in relationships.Values)
        {
            if (!string.Equals(relationship.Type, ChartRelationshipType, StringComparison.Ordinal))
            {
                continue;
            }

            fingerprint += AnalyzeChart(archive, relationship.Target);
        }

        return fingerprint;
    }

    private static int AnalyzeChart(ZipArchive archive, string chartPath)
    {
        XDocument chart = LoadXml(archive, chartPath);
        return chart.Descendants(s_chartNs + "f")
            .Sum(formula => formula.Value.Length);
    }

    private static int AnalyzeComments(ZipArchive archive, string? commentsPath)
    {
        if (string.IsNullOrWhiteSpace(commentsPath))
        {
            return 0;
        }

        XDocument comments = LoadXml(archive, commentsPath);
        return comments.Descendants(s_mainNs + "comment").Count();
    }

    private static Dictionary<string, RelationshipInfo> ReadRelationships(ZipArchive archive, string ownerPath)
    {
        string relationshipPath = BuildRelationshipsPath(ownerPath);
        ZipArchiveEntry? entry = TryGetEntry(archive, relationshipPath);
        if (entry is null)
        {
            return [];
        }

        XDocument relationships = LoadXml(entry);
        return relationships.Root?.Elements(s_pkgRelNs + "Relationship")
            .Where(relationship =>
                relationship.Attribute("Id") is not null &&
                relationship.Attribute("Type") is not null &&
                relationship.Attribute("Target") is not null)
            .ToDictionary(
                relationship => (string)relationship.Attribute("Id")!,
                relationship => new RelationshipInfo(
                    (string)relationship.Attribute("Id")!,
                    (string)relationship.Attribute("Type")!,
                    ResolveRelativePath(ownerPath, (string)relationship.Attribute("Target")!)),
                StringComparer.Ordinal)
            ?? [];
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

    private static string ResolveRelativePath(string ownerPath, string target)
    {
        string normalizedTarget = target.Replace('\\', '/');
        if (normalizedTarget.StartsWith("/", StringComparison.Ordinal))
        {
            return normalizedTarget.TrimStart('/');
        }

        int slash = ownerPath.LastIndexOf('/');
        string directory = slash >= 0 ? ownerPath[..slash] : string.Empty;
        string combined = string.IsNullOrEmpty(directory) ? normalizedTarget : $"{directory}/{normalizedTarget}";
        string[] segments = combined.Split('/', StringSplitOptions.RemoveEmptyEntries);
        var resolved = new List<string>(segments.Length);

        foreach (string segment in segments)
        {
            if (string.Equals(segment, ".", StringComparison.Ordinal))
            {
                continue;
            }

            if (string.Equals(segment, "..", StringComparison.Ordinal))
            {
                if (resolved.Count > 0)
                {
                    resolved.RemoveAt(resolved.Count - 1);
                }

                continue;
            }

            resolved.Add(segment);
        }

        return string.Join("/", resolved);
    }

    private static XDocument LoadXml(ZipArchive archive, string path)
    {
        ZipArchiveEntry entry = TryGetEntry(archive, path)
            ?? throw new InvalidOperationException($"Entry '{path}' was not found in the Open XML package.");
        return LoadXml(entry);
    }

    private static XDocument LoadXml(ZipArchiveEntry entry)
    {
        using Stream stream = entry.Open();
        return XDocument.Load(stream, LoadOptions.None);
    }

    private static ZipArchiveEntry? TryGetEntry(ZipArchive archive, string path)
    {
        string normalizedPath = path.Replace('\\', '/');
        return archive.GetEntry(normalizedPath)
            ?? archive.Entries.FirstOrDefault(entry =>
                string.Equals(entry.FullName.Replace('\\', '/'), normalizedPath, StringComparison.OrdinalIgnoreCase));
    }

    private sealed record RelationshipInfo(string Id, string Type, string Target);
}
