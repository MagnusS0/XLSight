using System.Text;
using XLSight.Analysis;
using XLSight.Internal.Packaging;
using XLSight.Internal.Readers.Xlsx;

namespace XLSight.Internal.Analysis;

internal static class ChartMetadataReader
{
    private const string ChartRelationshipType = "http://schemas.openxmlformats.org/officeDocument/2006/relationships/chart";

    private static ReadOnlySpan<byte> TagFormula => "f"u8;

    internal static List<ChartInfo> ReadCharts(XlsxPackage package, string sheetName, IReadOnlyList<string> drawingPaths)
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
        using Stream? stream = package.TryOpenEntryBuffered(chartPath);
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
                    refs.Add(NormalizeFormulaText(Encoding.UTF8.GetString(valueBytes)));
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

    private static IReadOnlyList<PackageRelationshipReader.RelationshipInfo> ReadRelationships(
        XlsxPackage package,
        string ownerPath)
    {
        string relPath = XlsxPackage.BuildRelationshipsPath(ownerPath);
        using Stream? relStream = package.TryOpenEntryBuffered(relPath);
        if (relStream is null)
        {
            return [];
        }

        return [.. PackageRelationshipReader.Read(relStream, ownerPath).Values];
    }

    private static string NormalizeFormulaText(string value)
    {
        if (!ShouldRemoveWorkbookIndexMarkers(value))
        {
            return value;
        }

        var builder = new StringBuilder(value.Length);
        for (int i = 0; i < value.Length; i++)
        {
            if (value[i] == '[')
            {
                int close = value.IndexOf("]!", i, StringComparison.Ordinal);
                if (close > i + 1 && IsAllDigits(value.AsSpan(i + 1, close - i - 1)))
                {
                    i = close + 1;
                    continue;
                }
            }

            builder.Append(value[i]);
        }

        return builder.ToString();
    }

    private static bool ShouldRemoveWorkbookIndexMarkers(string value) =>
        value.Contains("]!", StringComparison.Ordinal) &&
        !value.Contains('$', StringComparison.Ordinal) &&
        !value.Contains(':', StringComparison.Ordinal);

    private static bool IsAllDigits(ReadOnlySpan<char> value)
    {
        foreach (char c in value)
        {
            if (!char.IsAsciiDigit(c))
            {
                return false;
            }
        }

        return !value.IsEmpty;
    }
}
