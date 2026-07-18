using System.Text;
using XLSight.Analysis;
using XLSight.Internal.Packaging;
using XLSight.Internal.Readers.Xlsx;

namespace XLSight.Internal.Analysis;

internal static class ExternalLinkMetadataReader
{
    private const string ExternalLinkRelationshipSuffix = "/externalLink";
    private const string ExternalLinkPathRelationshipSuffix = "/externalLinkPath";
    private static ReadOnlySpan<byte> TagSheetName => "sheetName"u8;
    private static ReadOnlySpan<byte> TagDefinedName => "definedName"u8;
    private static ReadOnlySpan<byte> ValAttr => "val="u8;
    private static ReadOnlySpan<byte> NameAttr => "name="u8;

    internal static IReadOnlyList<ExternalWorkbookLinkInfo> Read(
        XlsxPackage package,
        string workbookPath,
        CancellationToken ct = default)
    {
        IReadOnlyList<PackageRelationshipReader.RelationshipInfo> workbookRelationships =
            AnalyzerMetadataReader.ReadRelationships(package, workbookPath, ct);
        var links = new List<ExternalWorkbookLinkInfo>();

        foreach (PackageRelationshipReader.RelationshipInfo relationship in workbookRelationships)
        {
            if (relationship.IsExternal ||
                !relationship.Type.EndsWith(ExternalLinkRelationshipSuffix, StringComparison.Ordinal))
            {
                continue;
            }

            ExternalWorkbookLinkInfo? link = ReadLink(package, relationship.Target, ct);
            if (link is not null)
            {
                links.Add(link);
            }
        }

        return links;
    }

    private static ExternalWorkbookLinkInfo? ReadLink(
        XlsxPackage package,
        string linkPartPath,
        CancellationToken ct)
    {
        IReadOnlyList<PackageRelationshipReader.RelationshipInfo> relationships =
            AnalyzerMetadataReader.ReadRelationships(package, linkPartPath, ct);
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
                ReadXmlMetadata(package, linkPartPath, sheetNames, definedNames, ct);
            }
            catch (Exception exception) when (exception is IOException or InvalidDataException)
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
        List<string> definedNames,
        CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        using Stream? stream = package.TryOpenEntryBuffered(linkPartPath);
        if (stream is null)
        {
            return;
        }

        using var ms = new MemoryStream();
        stream.CopyTo(ms);
        ct.ThrowIfCancellationRequested();
        ReadOnlySpan<byte> content = ms.GetBuffer().AsSpan(0, (int)ms.Length);

        ScanTagAttribute(content, TagSheetName, ValAttr, sheetNames);
        ScanTagAttribute(content, TagDefinedName, NameAttr, definedNames);
    }

    private static void ScanTagAttribute(
        ReadOnlySpan<byte> content,
        ReadOnlySpan<byte> tag,
        ReadOnlySpan<byte> attr,
        List<string> results)
    {
        ReadOnlySpan<byte> remaining = content;
        while (true)
        {
            var status = XmlByteReader.TryFindStartTag(remaining, tag, out StartTagMatch match, out _);
            if (status != TagSearchResult.Found)
            {
                break;
            }

            var attrBytes = remaining.Slice(match.AfterName, match.EndExclusive - match.AfterName);
            if (CellAttributeParser.TryGetAttributeValue(attrBytes, attr, out ReadOnlySpan<byte> valueBytes))
            {
                string value = Utf8CellDecoder.UnescapeXml(Encoding.UTF8.GetString(valueBytes));
                if (!string.IsNullOrWhiteSpace(value))
                {
                    results.Add(value);
                }
            }

            remaining = remaining[match.EndExclusive..];
        }
    }

}
