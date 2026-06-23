using System.Xml;

namespace XLSight.Internal.Packaging;

internal static class RelationshipsParser
{
    private const string WorkbookPath = "xl/workbook.xml";

    public static WorkbookMetadata Parse(
        Stream stream,
        WorkbookParser.ParsedWorkbookDefinition workbook)
    {
        ArgumentNullException.ThrowIfNull(stream);
        ArgumentNullException.ThrowIfNull(workbook);

        try
        {
            using var reader = XmlReader.Create(stream, CreateReaderSettings());
            var pathsByRelationshipId = new Dictionary<string, string>(StringComparer.Ordinal);

            while (reader.Read())
            {
                if (reader.NodeType != XmlNodeType.Element ||
                    !string.Equals(reader.LocalName, "Relationship", StringComparison.Ordinal))
                {
                    continue;
                }

                string? relationshipId = reader.GetAttribute("Id");
                string? target = reader.GetAttribute("Target");
                if (string.IsNullOrWhiteSpace(relationshipId) || string.IsNullOrWhiteSpace(target))
                {
                    continue;
                }

                pathsByRelationshipId[relationshipId] = ResolveWorkbookRelativePath(target);
            }

            return BuildMetadata(workbook, pathsByRelationshipId);
        }
        catch (XmlException exception)
        {
            throw new MalformedWorkbookException("Workbook relationships XML is corrupt.", exception);
        }
    }

    private static XmlReaderSettings CreateReaderSettings()
    {
        return new XmlReaderSettings
        {
            DtdProcessing = DtdProcessing.Prohibit,
            IgnoreComments = true,
            IgnoreWhitespace = true,
            CloseInput = false,
        };
    }

    private static WorkbookMetadata BuildMetadata(
        WorkbookParser.ParsedWorkbookDefinition workbook,
        Dictionary<string, string> pathsByRelationshipId)
    {
        var sheets = new List<WorkbookMetadata.WorkbookSheetInfo>(workbook.Sheets.Count);
        foreach (WorkbookParser.SheetDefinition sheet in workbook.Sheets)
        {
            if (!pathsByRelationshipId.TryGetValue(sheet.RelationshipId, out string? path))
            {
                throw new MalformedWorkbookException(
                    $"Workbook sheet '{sheet.Name}' is missing a relationship target.");
            }

            sheets.Add(new WorkbookMetadata.WorkbookSheetInfo(sheet.Name, path));
        }

        return new WorkbookMetadata(
            sheets,
            workbook.NamedRanges,
            workbook.UsesDate1904,
            workbook.HasMacros);
    }

    private static string ResolveWorkbookRelativePath(string target)
    {
        string normalizedTarget = target.Replace('\\', '/');
        if (normalizedTarget.StartsWith('/'))
        {
            return normalizedTarget.TrimStart('/');
        }

        int lastSlash = WorkbookPath.LastIndexOf('/');
        string workbookDirectory = lastSlash >= 0 ? WorkbookPath[..lastSlash] : string.Empty;
        string combinedPath = $"{workbookDirectory}/{normalizedTarget}";
        return NormalizeSegments(combinedPath);
    }

    private static string NormalizeSegments(string path)
    {
        string normalizedPath = path.Replace('\\', '/');
        string[] segments = normalizedPath.Split("/", StringSplitOptions.RemoveEmptyEntries);
        var resolvedSegments = new List<string>(segments.Length);

        foreach (string segment in segments)
        {
            if (string.Equals(segment, ".", StringComparison.Ordinal))
            {
                continue;
            }

            if (string.Equals(segment, "..", StringComparison.Ordinal))
            {
                if (resolvedSegments.Count > 0)
                {
                    resolvedSegments.RemoveAt(resolvedSegments.Count - 1);
                }

                continue;
            }

            resolvedSegments.Add(segment);
        }

        return string.Join("/", resolvedSegments);
    }
}
