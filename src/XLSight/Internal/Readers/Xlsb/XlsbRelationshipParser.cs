using System.Xml;

namespace XLSight.Internal.Readers.Xlsb;

internal static class XlsbRelationshipParser
{
    internal static Dictionary<string, string> Parse(Stream stream)
    {
        var relationships = new Dictionary<string, string>(StringComparer.Ordinal);
        using var reader = XmlReader.Create(stream, new XmlReaderSettings
        {
            DtdProcessing = DtdProcessing.Prohibit,
            IgnoreComments = true,
            IgnoreWhitespace = true,
            CloseInput = false,
        });

        while (reader.Read())
        {
            if (reader.NodeType != XmlNodeType.Element ||
                !string.Equals(reader.LocalName, "Relationship", StringComparison.Ordinal))
            {
                continue;
            }

            string? id = reader.GetAttribute("Id");
            string? target = reader.GetAttribute("Target");
            string? targetMode = reader.GetAttribute("TargetMode");
            if (string.Equals(targetMode, "External", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (!string.IsNullOrWhiteSpace(id) && !string.IsNullOrWhiteSpace(target))
            {
                relationships[id] = ResolveWorkbookRelativePath(target);
            }
        }

        return relationships;
    }

    private static string ResolveWorkbookRelativePath(string target)
    {
        string normalized = target.Replace('\\', '/');
        if (normalized.StartsWith('/'))
        {
            return normalized.TrimStart('/');
        }

        string combined = $"xl/{normalized}";
        var segments = combined.Split('/', StringSplitOptions.RemoveEmptyEntries);
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
