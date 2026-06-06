using System.Net;
using System.Text;
using XLSight.Internal.Packaging;
using XLSight.Internal.Readers.Xlsx;

namespace XLSight.Internal.Analysis;

internal static class PackageRelationshipReader
{
    private static ReadOnlySpan<byte> TagRelationship => "Relationship"u8;
    private static ReadOnlySpan<byte> IdAttr => "Id="u8;
    private static ReadOnlySpan<byte> TypeAttr => "Type="u8;
    private static ReadOnlySpan<byte> TargetAttr => "Target="u8;
    private static ReadOnlySpan<byte> TargetModeAttr => "TargetMode="u8;

    internal sealed record RelationshipInfo(string Id, string Type, string Target, bool IsExternal);

    internal static IReadOnlyDictionary<string, RelationshipInfo> Read(Stream stream, string ownerPath)
    {
        ArgumentNullException.ThrowIfNull(stream);

        try
        {
            using var buf = new ScanBuffer(stream);
            var relationships = new Dictionary<string, RelationshipInfo>(StringComparer.Ordinal);

            while (true)
            {
                var span = buf.Span;
                var status = XmlByteReader.TryFindStartTag(span, TagRelationship, out var match, out int partialIndex);
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

                var attrBytes = span.Slice(match.AfterName, match.EndExclusive - match.AfterName);
                if (CellAttributeParser.TryGetAttributeValue(attrBytes, IdAttr, out var idBytes) &&
                    CellAttributeParser.TryGetAttributeValue(attrBytes, TypeAttr, out var typeBytes) &&
                    CellAttributeParser.TryGetAttributeValue(attrBytes, TargetAttr, out var targetBytes))
                {
                    string id = Encoding.UTF8.GetString(idBytes);
                    string type = Encoding.UTF8.GetString(typeBytes);
                    string rawTarget = WebUtility.HtmlDecode(Encoding.UTF8.GetString(targetBytes));
                    bool isExternal = CellAttributeParser.TryGetAttributeValue(attrBytes, TargetModeAttr, out var modeBytes) &&
                        modeBytes.SequenceEqual("External"u8);
                    string target = isExternal
                        ? rawTarget
                        : PackageRelationshipReader.ResolveRelativePath(ownerPath, rawTarget);
                    relationships[id] = new RelationshipInfo(id, type, target, isExternal);
                }

                buf.Advance(match.EndExclusive);
            }

            return relationships;
        }
        catch (Exception exception) when (exception is not MalformedWorkbookException)
        {
            throw new MalformedWorkbookException($"Relationship part '{ownerPath}' is corrupt.", exception);
        }
    }

    internal static string ResolveRelativePath(string ownerPath, string target)
    {
        string normalizedTarget = PathNormalizer.Normalize(target);
        if (normalizedTarget.StartsWith('/'))
        {
            return normalizedTarget.TrimStart('/');
        }

        int slash = ownerPath.LastIndexOf('/');
        string directory = slash >= 0 ? ownerPath[..slash] : string.Empty;
        string combined = string.IsNullOrEmpty(directory) ? normalizedTarget : $"{directory}/{normalizedTarget}";
        string[] segments = PathNormalizer.Normalize(combined)
            .Split('/', StringSplitOptions.RemoveEmptyEntries);

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
}
