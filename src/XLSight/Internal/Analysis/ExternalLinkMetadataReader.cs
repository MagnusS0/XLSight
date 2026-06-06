using System.Xml;
using XLSight.Analysis;
using XLSight.Internal.Packaging;

namespace XLSight.Internal.Analysis;

internal static class ExternalLinkMetadataReader
{
    private const string ExternalLinkRelationshipSuffix = "/externalLink";
    private const string ExternalLinkPathRelationshipSuffix = "/externalLinkPath";

    internal static IReadOnlyList<ExternalWorkbookLinkInfo> Read(
        XlsxPackage package,
        string workbookPath)
    {
        if (!package.Entries.Any(static entry =>
                entry.FullName.StartsWith("xl/externalLinks/", StringComparison.OrdinalIgnoreCase)))
        {
            return [];
        }

        IReadOnlyList<PackageRelationshipReader.RelationshipInfo> workbookRelationships =
            ReadRelationships(package, workbookPath);
        var links = new List<ExternalWorkbookLinkInfo>();

        foreach (PackageRelationshipReader.RelationshipInfo relationship in workbookRelationships)
        {
            if (relationship.IsExternal ||
                !relationship.Type.EndsWith(ExternalLinkRelationshipSuffix, StringComparison.Ordinal))
            {
                continue;
            }

            ExternalWorkbookLinkInfo? link = ReadLink(package, relationship.Target);
            if (link is not null)
            {
                links.Add(link);
            }
        }

        return links;
    }

    private static ExternalWorkbookLinkInfo? ReadLink(XlsxPackage package, string linkPartPath)
    {
        IReadOnlyList<PackageRelationshipReader.RelationshipInfo> relationships =
            ReadRelationships(package, linkPartPath);
        PackageRelationshipReader.RelationshipInfo? targetRelationship = relationships.FirstOrDefault(
            static relationship => relationship.IsExternal &&
                relationship.Type.EndsWith(ExternalLinkPathRelationshipSuffix, StringComparison.Ordinal));
        targetRelationship ??= relationships.FirstOrDefault(static relationship => relationship.IsExternal);
        if (targetRelationship is null)
        {
            return null;
        }

        var sheetNames = new List<string>();
        var definedNames = new List<string>();
        if (linkPartPath.EndsWith(".xml", StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                ReadXmlMetadata(package, linkPartPath, sheetNames, definedNames);
            }
            catch (Exception exception) when (exception is XmlException or IOException or InvalidDataException)
            {
                sheetNames.Clear();
                definedNames.Clear();
            }
        }

        return new ExternalWorkbookLinkInfo
        {
            Target = targetRelationship.Target,
            SheetNames = sheetNames,
            DefinedNames = definedNames,
        };
    }

    private static void ReadXmlMetadata(
        XlsxPackage package,
        string linkPartPath,
        List<string> sheetNames,
        List<string> definedNames)
    {
        using Stream? stream = package.TryOpenEntryBuffered(linkPartPath);
        if (stream is null)
        {
            return;
        }

        var settings = new XmlReaderSettings
        {
            DtdProcessing = DtdProcessing.Prohibit,
            IgnoreComments = true,
            IgnoreWhitespace = true,
            XmlResolver = null,
        };
        using XmlReader reader = XmlReader.Create(stream, settings);
        while (reader.Read())
        {
            if (reader.NodeType != XmlNodeType.Element)
            {
                continue;
            }

            if (string.Equals(reader.LocalName, "sheetName", StringComparison.Ordinal))
            {
                AddAttributeValue(reader, "val", sheetNames);
            }
            else if (string.Equals(reader.LocalName, "definedName", StringComparison.Ordinal))
            {
                AddAttributeValue(reader, "name", definedNames);
            }
        }
    }

    private static void AddAttributeValue(XmlReader reader, string attributeName, List<string> values)
    {
        string? value = reader.GetAttribute(attributeName);
        if (!string.IsNullOrWhiteSpace(value))
        {
            values.Add(value);
        }
    }

    private static IReadOnlyList<PackageRelationshipReader.RelationshipInfo> ReadRelationships(
        XlsxPackage package,
        string ownerPath)
    {
        string relationshipPath = XlsxPackage.BuildRelationshipsPath(ownerPath);
        using Stream? stream = package.TryOpenEntryBuffered(relationshipPath);
        return stream is null ? [] : [.. PackageRelationshipReader.Read(stream, ownerPath).Values];
    }
}
